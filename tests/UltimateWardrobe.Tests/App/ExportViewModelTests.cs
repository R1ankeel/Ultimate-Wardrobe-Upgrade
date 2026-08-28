using FluentAssertions;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;
using System.IO;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Sprint 6.6 - Export screen (<see cref="ExportViewModel"/>), headless: pre-export checklist rollup,
/// output-folder default, allow-partial gating, the "build wardrobe" invocation of <see cref="IPatcher"/>
/// through the background task service with stage progress, the result card from
/// <see cref="PatchResult"/>/<see cref="PatchReport"/>, cancellation, and re-export.
/// </summary>
[Trait("Category", "App")]
public class ExportViewModelTests
{
    [Fact]
    public void Refresh_without_overhaul_shows_empty_state()
    {
        var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
        var session = new ProjectSession();
        session.Open(project, "C:/Projects/Test/project.db", new RecordingStore());
        var vm = new ExportViewModel(session, new OverhaulSelection(), new MappingService(project.Library), new ScriptedPatcher(),
            new DispatcherBackgroundTaskService(), new ScriptedSnackbarService());

        vm.Refresh();

        vm.IsEmpty.Should().BeTrue();
        vm.TotalSets.Should().Be(0);
    }

    [Fact]
    public void Refresh_computes_checklist_counts_from_mappings()
    {
        var (project, overhaul) = CreateChecklistProject();
        var vm = Build(project, overhaul, new ScriptedPatcher(), out _);

        vm.Refresh();

        vm.IsEmpty.Should().BeFalse();
        vm.TotalSets.Should().Be(4);
        vm.SetsNotStarted.Should().Be(1);
        vm.SetsInProgress.Should().Be(1);
        vm.SetsReady.Should().Be(1);
        vm.SetsNeedsPatch.Should().Be(1);
        vm.SetsDone.Should().Be(0);
    }

    [Fact]
    public void Default_output_folder_is_project_root_Export()
    {
        var (project, overhaul) = CreateChecklistProject();
        var vm = Build(project, overhaul, new ScriptedPatcher(), out _);

        vm.Refresh();

        vm.OutputFolder.Should().Be(Path.Combine(project.RootPath, "Export"));
    }

    [Fact]
    public void Build_is_blocked_when_not_all_ready_and_partial_disallowed()
    {
        var (project, overhaul) = CreateChecklistProject();
        var vm = Build(project, overhaul, new ScriptedPatcher(), out _);

        vm.Refresh();
        vm.AllowPartial.Should().BeFalse();
        vm.BuildCommand.CanExecute(null).Should().BeFalse("disabled while sets are unfinished");
    }

    [Fact]
    public async Task Allow_partial_enables_build_and_invokes_patcher()
    {
        var (project, overhaul) = CreateChecklistProject();
        var patcher = new ScriptedPatcher();
        var vm = Build(project, overhaul, patcher, out _);

        vm.Refresh();
        vm.AllowPartial = true;
        vm.BuildCommand.CanExecute(null).Should().BeTrue();
        vm.OutputFolder = "C:/Export";

        await vm.BuildCommand.ExecuteAsync(null);

        patcher.CallCount.Should().Be(1);
        patcher.OutputDirs.Should().ContainSingle().Which.Should().Be("C:/Export");
        patcher.Overhauls.Should().ContainSingle().Which.Id.Should().Be(overhaul.Id);
    }

    [Fact]
    public async Task Build_renders_result_card_from_patch_report()
    {
        var (project, overhaul) = CreateChecklistProject();
        var patcher = new ScriptedPatcher();
        var vm = Build(project, overhaul, patcher, out var snackbar);

        vm.Refresh();
        vm.AllowPartial = true;
        vm.OutputFolder = "C:/Export";

        await vm.BuildCommand.ExecuteAsync(null);

        patcher.CallCount.Should().Be(1);
        vm.IsBuilding.Should().BeFalse();
        vm.IsResultVisible.Should().BeTrue();
        vm.ResultPluginPath.Should().EndWith(".esp");
        vm.OverriddenRecords.Should().Be(12);
        vm.CopiedFilesCount.Should().Be(2);
        vm.CopiedBytesText.Should().NotBeNullOrWhiteSpace();
        vm.HasWarnings.Should().BeTrue();
        vm.ResultWarnings.Should().ContainSingle();
        snackbar.Shown.Should().Contain(s => s.Title == "Export complete");
    }

    [Fact]
    public async Task Build_surfaces_patch_progress_stages()
    {
        var (project, overhaul) = CreateChecklistProject();
        var patcher = new ScriptedPatcher();
        var vm = Build(project, overhaul, patcher, out _);

        vm.Refresh();
        vm.AllowPartial = true;
        vm.OutputFolder = "C:/Export";

        await vm.BuildCommand.ExecuteAsync(null);

        patcher.Reported.Should().NotBeEmpty();
        patcher.Reported[0].Select(p => p.Stage).Should().Contain("Build esp plugin");
        patcher.Reported[0].Select(p => p.Stage).Should().HaveCountGreaterThanOrEqualTo(5);
        vm.IsResultVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Re_export_builds_again_and_relabels_button()
    {
        var (project, overhaul) = CreateChecklistProject();
        var patcher = new ScriptedPatcher();
        var vm = Build(project, overhaul, patcher, out _);

        vm.Refresh();
        vm.AllowPartial = true;
        vm.OutputFolder = "C:/Export";

        await vm.BuildCommand.ExecuteAsync(null);
        vm.BuildButtonLabel.Should().Be("Re-export");
        await vm.BuildCommand.ExecuteAsync(null);

        patcher.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Cancelled_build_shows_cancelled_state_and_no_result()
    {
        var (project, overhaul) = CreateChecklistProject();
        var patcher = new ScriptedPatcher
        {
            OnBuild = static async (_, _, _, _, _) =>
            {
                await Task.Yield();
                throw new OperationCanceledException();
            },
        };
        var vm = Build(project, overhaul, patcher, out var snackbar);

        vm.Refresh();
        vm.AllowPartial = true;
        vm.OutputFolder = "C:/Export";

        await vm.BuildCommand.ExecuteAsync(null);

        vm.IsBuilding.Should().BeFalse();
        vm.IsResultVisible.Should().BeFalse();
        snackbar.Shown.Should().Contain(s => s.Title == "Export cancelled");
    }

    [Fact]
    public async Task Failed_build_raises_alert_and_no_result()
    {
        var (project, overhaul) = CreateChecklistProject();
        var patcher = new ScriptedPatcher
        {
            OnBuild = static (_, _, _, _, _) => throw new InvalidOperationException("boom"),
        };
        var dialogs = new ScriptedDialogService();
        var session = new ProjectSession();
        session.Open(project, "C:/Projects/Test/project.db", new RecordingStore());
        var selection = new OverhaulSelection();
        selection.Select(overhaul.Id);
        var vm = new ExportViewModel(session, selection, new MappingService(project.Library), patcher,
            new DispatcherBackgroundTaskService(), new ScriptedSnackbarService(), dialogs);

        vm.Refresh();
        vm.AllowPartial = true;
        vm.OutputFolder = "C:/Export";

        await vm.BuildCommand.ExecuteAsync(null);

        vm.IsResultVisible.Should().BeFalse();
        dialogs.Alerts.Should().ContainSingle().Which.Title.Should().Be("Export failed");
    }

    private static ExportViewModel Build(
        Project project, Overhaul overhaul, ScriptedPatcher patcher, out ScriptedSnackbarService snackbar)
    {
        snackbar = new ScriptedSnackbarService();
        var session = new ProjectSession();
        session.Open(project, "C:/Projects/Test/project.db", new RecordingStore());
        var selection = new OverhaulSelection();
        selection.Select(overhaul.Id);
        return new ExportViewModel(session, selection, new MappingService(project.Library), patcher,
            new DispatcherBackgroundTaskService(), snackbar);
    }

    /// <summary>
    /// Four single-piece FEMALE sets forced to one stable status each via mappings: NoMapSet
    /// (NotStarted), PartialSet (InProgress, one of two pieces mapped), FullSet (Mapped) and
    /// PatchSet (NeedsPatch).
    /// </summary>
    private static (Project Project, Overhaul Overhaul) CreateChecklistProject()
    {
        var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
        var donor = new DonorAsset(Guid.NewGuid(), "donor.7z", "C:/Src/d", DateTime.UtcNow, "h", DonorAssetKind.FullReplacer,
            new[] { new DonorProvidedSet("s", "D") });
        project.Library.Assets.Add(donor);

        var catalog = new Catalog(new VanillaCatalogSource("C:/Game"),
            new[]
            {
                Set("NoMapSet", "No Map Set", V(Gender.Female, WeightClass.Heavy, P("n0"))),
                Set("PartialSet", "Partial Set", V(Gender.Female, WeightClass.Heavy, P("p0"), P("p1"))),
                Set("FullSet", "Full Set", V(Gender.Female, WeightClass.Heavy, P("f0"))),
                Set("PatchSet", "Patch Set", V(Gender.Female, WeightClass.Heavy, P("w0"))),
            });

        var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game"))
        {
            Catalog = catalog,
        };
        overhaul.Mappings.Add(NewMapping("PartialSet", "p0", donor, MappingStatus.Mapped));
        overhaul.Mappings.Add(NewMapping("FullSet", "f0", donor, MappingStatus.Mapped));
        overhaul.Mappings.Add(NewMapping("PatchSet", "w0", donor, MappingStatus.NeedsPatch));
        project.Overhauls.Add(overhaul);
        return (project, overhaul);
    }

    private static PieceMapping NewMapping(string setId, string pieceEditor, DonorAsset donor, MappingStatus status)
        => new(Guid.NewGuid(), Guid.NewGuid(), setId, pieceEditor, Gender.Female, donor.ImportId, "D", "d.nif", status: status);

    private static ArmorSet Set(string id, string name, params Variant[] variants) => new(id, name, variants);
    private static Variant V(Gender gender, WeightClass weight, params Piece[] pieces) => new(gender, weight, pieces);
    private static Piece P(string editorId) => new(editorId, 0x12345678, "32 Body", editorId + "Arma", $"armor/{editorId}.nif");
}
