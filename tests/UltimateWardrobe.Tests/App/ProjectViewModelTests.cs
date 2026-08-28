using FluentAssertions;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.App.Views;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Mapping;
using UltimateWardrobe.Tests.Mapping;
using UltimateWardrobe.Tests.Persistence;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Sprint 6.2 - <see cref="ProjectViewModel"/> overhaul cards over a stub store + temp dirs (headless):
/// add vanilla overhaul (picker + validator passes, graph mutated, autosaved), cancelled picker no-ops,
/// rename rebuilds the immutable Overhaul and autosaves, delete confirms + removes + autosaves, select
/// navigates to <see cref="OverhaulView"/>, a catalog-backed overhaul renders progress counts, and -
/// like the picker - no switch/close project command leaks out.
/// </summary>
[Trait("Category", "App")]
public class ProjectViewModelTests
{
    [Fact]
    public async Task AddVanillaOverhaul_adds_card_autosaves_and_uses_folder_name()
    {
        var gameRoot = TestHelpers.NewTempDir("UW_VmGame_");
        try
        {
            Directory.CreateDirectory(Path.Combine(gameRoot, "Data"));
            File.WriteAllText(Path.Combine(gameRoot, "Data", "Skyrim.esm"), string.Empty);

            var h = BuildViewModel();
            h.Vm.Refresh();
            h.Scripted.PickFolder = (_, _) => gameRoot;

            var before = h.Session.Project!.Overhauls.Count;
            await h.Vm.AddVanillaOverhaulCommand.ExecuteAsync(null);

            h.Vm.Overhauls.Should().HaveCount(before + 1);
            h.Session.Project.Overhauls.Should().HaveCount(before + 1);
            h.Session.Project.Overhauls.Last().Name.Should()
                .Be(new DirectoryInfo(gameRoot).Name, "the card starts at the source folder name");
            h.Store.SaveCount.Should().Be(before + 1, "each mutation autosaves through the shared store");
            h.Vm.Overhauls.Single().TotalSets.Should().Be(0);
            h.Vm.Overhauls.Single().StatusLabel.Should().Be("No catalog - run a scan");
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(gameRoot);
        }
    }

    [Fact]
    public async Task CancelledPicker_adds_nothing()
    {
        var h = BuildViewModel();
        h.Vm.Refresh();
        h.Scripted.PickFolder = (_, _) => null;

        var before = h.Session.Project!.Overhauls.Count;
        await h.Vm.AddVanillaOverhaulCommand.ExecuteAsync(null);

        h.Vm.Overhauls.Should().HaveCount(before);
        h.Store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task RenameOverhaul_rebuilds_immutable_overhaul_and_autosaves()
    {
        var h = BuildViewModel();
        h.Session.Project!.Overhauls.Add(new Overhaul(Guid.NewGuid(), "Old", h.Session.Project.Id, new VanillaCatalogSource("C:\\G")));
        h.Vm.Refresh();

        h.Scripted.PromptText = (_, _, _) => "New Name";
        await h.Vm.RenameOverhaulCommand.ExecuteAsync(h.Vm.Overhauls.Single());

        h.Vm.Overhauls.Single().Name.Should().Be("New Name");
        h.Session.Project.Overhauls.Single().Name.Should().Be("New Name");
        h.Store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task DeleteOverhaul_after_confirm_removes_and_autosaves()
    {
        var h = BuildViewModel();
        h.Session.Project!.Overhauls.Add(new Overhaul(Guid.NewGuid(), "Doomed", h.Session.Project.Id, new VanillaCatalogSource("C:\\G")));
        h.Vm.Refresh();

        h.Scripted.ConfirmResult = true;
        await h.Vm.DeleteOverhaulCommand.ExecuteAsync(h.Vm.Overhauls.Single());

        h.Vm.Overhauls.Should().BeEmpty();
        h.Session.Project.Overhauls.Should().BeEmpty();
        h.Store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task DeleteOverhaul_when_not_confirmed_leaves_graph_intact()
    {
        var h = BuildViewModel();
        h.Session.Project!.Overhauls.Add(new Overhaul(Guid.NewGuid(), "Kept", h.Session.Project.Id, new VanillaCatalogSource("C:\\G")));
        h.Vm.Refresh();

        h.Scripted.ConfirmResult = false;
        await h.Vm.DeleteOverhaulCommand.ExecuteAsync(h.Vm.Overhauls.Single());

        h.Vm.Overhauls.Should().ContainSingle();
        h.Session.Project.Overhauls.Should().ContainSingle();
        h.Store.SaveCount.Should().Be(0);
    }

    [Fact]
    public void SelectOverhaul_navigates_to_overhaul_matrix_screen()
    {
        var h = BuildViewModel();
        h.Session.Project!.Overhauls.Add(new Overhaul(Guid.NewGuid(), "Vigilant", h.Session.Project.Id, new VanillaCatalogSource("C:\\G")));
        h.Vm.Refresh();

        h.Vm.SelectOverhaulCommand.Execute(h.Vm.Overhauls.Single());

        h.Scripts.Navigated.Should().Contain(typeof(OverhaulView));
    }

    [Fact]
    public void Catalog_backed_overhaul_renders_progress_counts()
    {
        var catalog = SyntheticCatalogUniverse.CreateIronCatalog();
        var h = BuildViewModel();
        h.Session.Project!.Overhauls.Add(new Overhaul(Guid.NewGuid(), "Iron", h.Session.Project.Id, new VanillaCatalogSource("C:\\G"))
            { Catalog = catalog });
        h.Vm.Refresh();

        var card = h.Vm.Overhauls.Single();
        card.TotalSets.Should().BeGreaterThan(0);
        card.MappedCount.Should().Be(0);
        card.StatusLabel.Should().Be("Not started");
    }

    [Fact]
    public void No_switch_or_close_project_command_is_exposed()
    {
        CommandNameLeak.Check(typeof(ProjectViewModel));
    }

    private static Host BuildViewModel()
    {
        var store = new RecordingStore();
        var session = new ProjectSession();
        var project = new Project(Guid.NewGuid(), "Test", "C:\\TestProject");
        session.Open(project, "C:\\TestProject\\project.db", store);

        var scripted = new ScriptedDialogService();
        var scripts = new RecordingNavigation();
        var vm = new ProjectViewModel(
            session,
            scripts,
            scripted,
            new OverhaulSourceValidator(),
            new MappingService(project.Library));

        return new Host(session, store, vm, scripted, scripts);
    }

    private sealed record Host(
        ProjectSession Session,
        RecordingStore Store,
        ProjectViewModel Vm,
        ScriptedDialogService Scripted,
        RecordingNavigation Scripts);
}
