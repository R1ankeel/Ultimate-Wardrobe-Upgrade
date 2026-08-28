using FluentAssertions;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;
using UltimateWardrobe.Tests.Persistence;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Sprint 6.3 - <see cref="DonorLibraryViewModel"/> donor table + import flow over a scripted
/// <see cref="IDonorImportRunner"/>, a stub classifier/extractor-backed <see cref="DonorLibraryService"/>
/// and a real (headless) <see cref="DispatcherBackgroundTaskService"/>: supported-only filtering before
/// the runner, success appends + autosave + progress, cancel surfaces no dialog, a failed batch shows a
/// typed alert and leaves the library untouched, remove (confirmed/unconfirmed), reclassify rebuilds the
/// asset, and the manual Kind override swaps the immutable asset in place. 0 failures / 0 warnings is the
/// Sprint gate.
/// </summary>
[Trait("Category", "App")]
public class DonorLibraryViewModelTests : IDisposable
{
    private readonly string _root = TestHelpers.NewTempDir("UW_DonorVm_");

    public void Dispose() => TestHelpers.DeleteDirectoryRetry(_root);

    [Fact]
    public async Task Import_filters_to_supported_extensions_before_runner()
    {
        var h = Build();
        h.Vm.Refresh();
        var unsupported = Path.Combine(_root, "readme.txt");
        string? archive = null;
        try
        {
            File.WriteAllText(unsupported, "hi");
            archive = Path.Combine(_root, "body.zip");
            File.WriteAllText(archive, "zip-bytes");

            await h.Vm.ImportCommand.ExecuteAsync(new[] { unsupported, archive });

            h.Runner.Calls.Should().HaveCount(1);
            h.Runner.LastPaths.Should().ContainSingle().And.Contain(archive);
            h.Session.Project!.Library.Assets.Should().ContainSingle();
            h.Store.SaveCount.Should().Be(1, "an appended asset autosaves through the shared store");
            h.Vm.Donors.Should().ContainSingle();
            h.Vm.IsImporting.Should().BeFalse();
            h.Scripted.Alerts.Should().BeEmpty();
        }
        finally
        {
            if (archive is not null) File.Delete(archive);
        }
    }

    [Fact]
    public async Task Import_with_no_supported_paths_is_a_noop()
    {
        var h = Build();
        h.Vm.Refresh();

        await h.Vm.ImportCommand.ExecuteAsync(new[] { Path.Combine(_root, "readme.txt") });

        h.Runner.Calls.Should().BeEmpty();
        h.Session.Project!.Library.Assets.Should().BeEmpty();
        h.Store.SaveCount.Should().Be(0);
        h.Vm.IsImporting.Should().BeFalse();
    }

    [Fact]
    public async Task Import_propagates_catalog_hint()
    {
        var gameRoot = TestHelpers.NewTempDir("UW_DonorHint_");
        try
        {
            var h = Build();
            h.Session.Project!.Overhauls.Add(new Overhaul(Guid.NewGuid(), "Vanilla", h.Session.Project.Id, new VanillaCatalogSource(gameRoot)));
            h.Vm.Refresh();
            var archive = Path.Combine(_root, "body.7z");
            File.WriteAllText(archive, "bytes");

            await h.Vm.ImportCommand.ExecuteAsync(new[] { archive });

            h.Runner.LastHint.Should().NotBeNull();
            h.Runner.LastHint!.Source.Should().BeOfType<VanillaCatalogSource>();
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(gameRoot);
        }
    }

    [Fact]
    public async Task Import_success_appends_assets_and_autosaves()
    {
        var h = Build();
        h.Vm.Refresh();
        var a = Path.Combine(_root, "a.zip");
        var b = Path.Combine(_root, "b.7z");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        try
        {
            await h.Vm.ImportCommand.ExecuteAsync(new[] { a, b });

            h.Session.Project!.Library.Assets.Should().HaveCount(2);
            h.Store.SaveCount.Should().Be(1);
            h.Vm.Donors.Should().HaveCount(2);
            h.Vm.ProgressValue.Should().Be(2);
            h.Vm.ProgressTotal.Should().Be(2);
            h.Vm.IsImporting.Should().BeFalse();
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
        }
    }

    [Fact]
    public async Task Import_cancel_surfaces_no_dialog()
    {
        var h = Build();
        h.Vm.Refresh();
        h.Runner.OnImport = static (_, _, _, _, _, _) =>
            throw new OperationCanceledException("cancelled");
        var a = Path.Combine(_root, "a.zip");
        File.WriteAllText(a, "a");
        try
        {
            await h.Vm.ImportCommand.ExecuteAsync(new[] { a });

            h.Scripted.Alerts.Should().BeEmpty();
            h.Session.Project!.Library.Assets.Should().BeEmpty();
            h.Vm.IsImporting.Should().BeFalse();
        }
        finally
        {
            File.Delete(a);
        }
    }

    [Fact]
    public async Task Import_failure_shows_alert_and_leaves_library_unchanged()
    {
        var h = Build();
        h.Vm.Refresh();
        h.Runner.OnImport = static (_, _, _, _, _, _) =>
            throw new InvalidOperationException("bad archive");
        var a = Path.Combine(_root, "a.zip");
        File.WriteAllText(a, "a");
        try
        {
            await h.Vm.ImportCommand.ExecuteAsync(new[] { a });

            h.Scripted.Alerts.Should().ContainSingle(x => x.Title == "Import failed");
            h.Session.Project!.Library.Assets.Should().BeEmpty();
            h.Store.SaveCount.Should().Be(0, "a failed batch appends nothing, so there is nothing to save");
            h.Vm.IsImporting.Should().BeFalse();
        }
        finally
        {
            File.Delete(a);
        }
    }

    [Fact]
    public async Task Remove_confirmed_removes_row_and_autosaves()
    {
        var h = Build();
        var asset = NewAsset("body.zip", DonorAssetKind.FullReplacer);
        h.Session.Project!.Library.Assets.Add(asset);
        h.Vm.Refresh();
        h.Scripted.ConfirmResult = true;

        await h.Vm.RemoveCommand.ExecuteAsync(h.Vm.Donors.Single());

        h.Session.Project.Library.Assets.Should().BeEmpty();
        h.Vm.Donors.Should().BeEmpty();
        h.Store.SaveCount.Should().Be(1);
        h.Scripted.Confirms.Should().ContainSingle();
    }

    [Fact]
    public async Task Remove_when_not_confirmed_leaves_library_intact()
    {
        var h = Build();
        h.Session.Project!.Library.Assets.Add(NewAsset("body.zip", DonorAssetKind.FullReplacer));
        h.Vm.Refresh();
        h.Scripted.ConfirmResult = false;

        await h.Vm.RemoveCommand.ExecuteAsync(h.Vm.Donors.Single());

        h.Session.Project.Library.Assets.Should().ContainSingle();
        h.Store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Reclassify_rebuilds_asset_from_stub_classifier_and_autosaves()
    {
        var h = Build();
        var extracted = Path.Combine(_root, "Src");
        Directory.CreateDirectory(extracted);
        var asset = new DonorAsset(
            Guid.NewGuid(), "physics.7z", extracted, DateTime.UtcNow, "h1",
            DonorAssetKind.FullReplacer, null, null,
            new[] { "body.nif" }, Array.Empty<string>());
        h.Session.Project!.Library.Assets.Add(asset);
        h.Classifier.NextKind = DonorAssetKind.PhysicsPatch;
        h.Vm.Refresh();

        await h.Vm.ReclassifyCommand.ExecuteAsync(h.Vm.Donors.Single());

        h.Session.Project.Library.Assets.Single().Kind.Should().Be(DonorAssetKind.PhysicsPatch);
        h.Vm.Donors.Single().KindText.Should().Be("Physics patch");
        h.Store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task SetKind_manual_override_swaps_immutable_asset_in_place()
    {
        var h = Build();
        h.Session.Project!.Library.Assets.Add(NewAsset("body.zip", DonorAssetKind.FullReplacer));
        h.Vm.Refresh();
        h.Scripted.PromptText = (_, _, _) => "Physics patch";

        await h.Vm.SetKindCommand.ExecuteAsync(h.Vm.Donors.Single());

        h.Session.Project.Library.Assets.Single().Kind.Should().Be(DonorAssetKind.PhysicsPatch);
        h.Vm.Donors.Single().KindText.Should().Be("Physics patch");
        h.Store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task SetKind_with_unrecognized_prompt_is_a_noop()
    {
        var h = Build();
        h.Session.Project!.Library.Assets.Add(NewAsset("body.zip", DonorAssetKind.FullReplacer));
        h.Vm.Refresh();
        h.Scripted.PromptText = (_, _, _) => "bogus kind";

        await h.Vm.SetKindCommand.ExecuteAsync(h.Vm.Donors.Single());

        h.Session.Project.Library.Assets.Single().Kind.Should().Be(DonorAssetKind.FullReplacer);
        h.Store.SaveCount.Should().Be(0);
    }

    [Fact]
    public void No_switch_or_close_project_command_is_exposed()
    {
        CommandNameLeak.Check(typeof(DonorLibraryViewModel));
    }

    private static DonorAsset NewAsset(string name, DonorAssetKind kind)
        => new(Guid.NewGuid(), name, Path.Combine("C:\\Src", Guid.NewGuid().ToString()), DateTime.UtcNow, "h", kind);

    private Host Build()
    {
        var store = new RecordingStore();
        var session = new ProjectSession();
        session.Open(new Project(Guid.NewGuid(), "Test", _root), Path.Combine(_root, "project.db"), store);

        var scripted = new ScriptedDialogService();
        var runner = new ScriptedDonorImportRunner();
        var classifier = new StubClassifier();
        var service = new DonorLibraryService(new DonorImportService(new StubExtractor()), classifier);
        var vm = new DonorLibraryViewModel(session, new DispatcherBackgroundTaskService(), runner, service, scripted);

        return new Host(session, store, vm, scripted, runner, classifier);
    }

    private sealed record Host(
        ProjectSession Session,
        RecordingStore Store,
        DonorLibraryViewModel Vm,
        ScriptedDialogService Scripted,
        ScriptedDonorImportRunner Runner,
        StubClassifier Classifier);

    private sealed class StubClassifier : IDonorClassifier
    {
        public DonorAssetKind NextKind = DonorAssetKind.FullReplacer;

        public Task<DonorAsset> ClassifyAsync(string extractedDir, Catalog? catalogHint = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new DonorAsset(
                Guid.NewGuid(), Path.GetFileName(extractedDir), extractedDir, DateTime.UtcNow, "class-hash", NextKind));
    }

    private sealed class StubExtractor : IArchiveExtractor
    {
        public Task<ExtractResult> ExtractAsync(string archivePath, string destDir, IProgress<ExtractProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(destDir);
            File.WriteAllText(Path.Combine(destDir, "body.nif"), "nif");
            return Task.FromResult(new ExtractResult(
                new[] { Path.Combine(destDir, "body.nif") }, 0, ArchiveFormat.Zip, "stub"));
        }
    }
}
