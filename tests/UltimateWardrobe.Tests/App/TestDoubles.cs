using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Persistence;
using FluentAssertions;
using System.IO;
using System.Reflection;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Scriptable <see cref="IAppDialogService"/> for headless ViewModel tests (Phase 6 Sprint 6.2).
/// Each picker/prompt is backed by a delegate you override per test; alerts/confirms are recorded so
/// a test can assert what was surfaced without any UI.
/// </summary>
internal sealed class ScriptedDialogService : IAppDialogService
{
    public Func<string?, string?, string?> PickProjectFolder = (_, _) => null;
    public Func<string?, string?, string?> PickFolder = (_, _) => null;
    public Func<string?, string?> PickModArchive = _ => null;
    public Func<string?, string?, string?, string?> PromptText = (_, _, _) => null;
    public bool ConfirmResult = true;

    public List<(string Title, string Message)> Alerts { get; } = new();
    public List<(string Title, string Message)> Confirms { get; } = new();
    public int FolderPickCount;

    public Task<string?> PickProjectFolderAsync(string title, string initialDirectory)
        => Task.FromResult(PickProjectFolder(title, initialDirectory));

    public Task<string?> PickFolderAsync(string title, string initialDirectory)
    {
        FolderPickCount++;
        return Task.FromResult(PickFolder(title, initialDirectory));
    }

    public Task<string?> PickModArchiveAsync(string title)
        => Task.FromResult(PickModArchive(title));

    public Task<string?> PromptTextAsync(string title, string message, string? initialValue = null)
        => Task.FromResult(PromptText(title, message, initialValue));

    public Task<bool> ConfirmAsync(string title, string message)
    {
        Confirms.Add((title, message));
        return Task.FromResult(ConfirmResult);
    }

    public Task AlertAsync(string title, string message)
    {
        Alerts.Add((title, message));
        return Task.CompletedTask;
    }
}

/// <summary>
/// <see cref="IProjectStore"/> stub that records calls and returns a preset project from
/// <see cref="LoadAsync"/> (Phase 6 Sprint 6.2).
/// </summary>
internal sealed class RecordingStore : IProjectStore
{
    public int SaveCount;
    public readonly List<Project> SavedProjects = new();

    public Task SaveAsync(Project project, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        SavedProjects.Add(project);
        return Task.CompletedTask;
    }

    public Task<Project> LoadAsync(string projectDbPath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Loading through the stub store is not used by Sprint 6.2 tests.");
}

/// <summary>
/// <see cref="IProjectStoreFactory"/> stub returning a shared <see cref="RecordingStore"/>.
/// </summary>
internal sealed class RecordingStoreFactory : IProjectStoreFactory
{
    public RecordingStore Store { get; } = new();

    public IProjectStore Open(string projectDbPath) => Store;
}

/// <summary>
/// <see cref="IAppNavigationService"/> stub recording every <see cref="IAppNavigationService.Navigate"/>
/// target (Phase 6 Sprint 6.2).
/// </summary>
internal sealed class RecordingNavigation : IAppNavigationService
{
    public List<Type> Navigated { get; } = new();

    public bool Navigate(Type pageType)
    {
        Navigated.Add(pageType);
        return true;
    }

    public bool GoBack() => false;
}

/// <summary>Records every <see cref="ISnackbarService.Show"/> call for headless VM tests (Sprint 6.6).</summary>
internal sealed class ScriptedSnackbarService : ISnackbarService
{
    public List<(string Title, string Message)> Shown { get; } = new();

    public void Show(string title, string message)
    {
        Shown.Add((title, message));
    }
}

/// <summary>
/// Scriptable <see cref="IPatcher"/> for headless <see cref="ExportViewModel"/> tests (Sprint 6.6).
/// The default <see cref="OnBuild"/> reports all five stages and returns a fabricated
/// <see cref="PatchResult"/> with a report; tests can override it to force a cancel or a failure.
/// </summary>
internal sealed class ScriptedPatcher : IPatcher
{
    public Func<Overhaul, UltimateWardrobe.Core.Domain.DonorLibrary, string, IProgress<PatchProgress>?, CancellationToken, Task<PatchResult>> OnBuild =
        static async (_, _, _, progress, _) =>
        {
            string[] stages = { "Resolve targets", "Prepare export folder", "Build esp plugin", "Copy donor files", "Write meta.ini" };
            for (var i = 0; i < stages.Length; i++)
            {
                progress?.Report(new PatchProgress(stages[i], i + 1, stages.Length));
                await Task.Yield();
            }

            return new PatchResult(@"C:\Export\UltimateWardrobe - Iron.esp", new[] { "meshes\\1.nif", "meshes\\2.nif" })
            {
                Report = new PatchReport
                {
                    TotalMappings = 3,
                    ResolvedMappings = 3,
                    SkippedMappings = 0,
                    OverriddenRecords = 12,
                    CopiedFiles = new[] { "meshes\\1.nif", "meshes\\2.nif" },
                    CopiedBytes = 2048,
                    Warnings = new[] { new PatchWarning("Donor mesh skipped.", "IronCuirassF") },
                },
            };
        };

    public List<string> OutputDirs { get; } = new();
    public List<Overhaul> Overhauls { get; } = new();
    public List<UltimateWardrobe.Core.Domain.DonorLibrary> Libraries { get; } = new();
    public List<List<PatchProgress>> Reported { get; } = new();
    public int CallCount { get; private set; }

    public async Task<PatchResult> BuildAsync(
        Overhaul overhaul,
        UltimateWardrobe.Core.Domain.DonorLibrary donorLibrary,
        string outputDir,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        Overhauls.Add(overhaul);
        Libraries.Add(donorLibrary);
        OutputDirs.Add(outputDir);

        Reported.Add(new List<PatchProgress>());
        var recorder = new ProgressRecorder(progress, Reported);
        var result = await OnBuild(overhaul, donorLibrary, outputDir, recorder, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private sealed class ProgressRecorder : IProgress<PatchProgress>
    {
        private readonly IProgress<PatchProgress>? _inner;
        private readonly List<List<PatchProgress>> _reported;

        public ProgressRecorder(IProgress<PatchProgress>? inner, List<List<PatchProgress>> reported)
        {
            _inner = inner;
            _reported = reported;
        }

        public void Report(PatchProgress value)
        {
            _reported[^1].Add(value);
            _inner?.Report(value);
        }
    }
}

/// <summary>
/// Scriptable <see cref="IDonorImportRunner"/> for headless <see cref="DonorLibraryViewModel"/> tests
/// (Phase 6 Sprint 6.3). Records every batch plus the reported progress snapshots, and delegates the
/// actual work to <see cref="OnImport"/> so a test can force success, a per-file append, a throw, or a
/// cancel without touching real archives.
/// </summary>
internal sealed class ScriptedDonorImportRunner : IDonorImportRunner
{
    public Func<IReadOnlyList<string>, string, UltimateWardrobe.Core.Domain.DonorLibrary, Catalog?, CancellationToken, IProgress<DonorImportProgress>?, Task<IReadOnlyList<DonorAsset>>> OnImport =
        static (paths, _, library, _, _, progress) =>
        {
            var done = new List<DonorAsset>();
            var i = 0;
            foreach (var path in paths)
            {
                var asset = new DonorAsset(
                    Guid.NewGuid(),
                    Path.GetFileName(path),
                    Path.Combine("C:\\Src", Guid.NewGuid().ToString()),
                    DateTime.UtcNow,
                    $"h{i}",
                    DonorAssetKind.FullReplacer);
                library.Assets.Add(asset);
                done.Add(asset);
                i++;
                progress?.Report(new DonorImportProgress(i, paths.Count));
            }

            return Task.FromResult<IReadOnlyList<DonorAsset>>(done);
        };

    public List<DonorImportProgress> Reported { get; } = new();
    public List<(IReadOnlyList<string> Paths, string ProjectRoot, Catalog? Hint)> Calls { get; } = new();

    public async Task<IReadOnlyList<DonorAsset>> ImportAsync(
        IReadOnlyList<string> archivePaths,
        string projectRoot,
        UltimateWardrobe.Core.Domain.DonorLibrary library,
        Catalog? catalogHint,
        IProgress<DonorImportProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((archivePaths, projectRoot, catalogHint));

        var result = await OnImport(archivePaths, projectRoot, library, catalogHint, cancellationToken,
            new InvokeProgress(progress, Reported));
        return result;
    }

    public IReadOnlyList<string> LastPaths => Calls.Count > 0 ? Calls[^1].Paths : Array.Empty<string>();
    public Catalog? LastHint => Calls.Count > 0 ? Calls[^1].Hint : null;

    /// <summary>Forwards report calls to the VM progress and also records them for assertions.</summary>
    private sealed class InvokeProgress : IProgress<DonorImportProgress>
    {
        private readonly IProgress<DonorImportProgress>? _inner;
        private readonly List<DonorImportProgress> _recorded;

        public InvokeProgress(IProgress<DonorImportProgress>? inner, List<DonorImportProgress> recorded)
        {
            _inner = inner;
            _recorded = recorded;
        }

        public void Report(DonorImportProgress value)
        {
            _recorded.Add(value);
            _inner?.Report(value);
        }
    }
}

/// <summary>
/// Reflection guard (Phase 6 Sprint 6.2): guarantees a view model does not leak a switch/close
/// project command into the shell - such shell-level project lifecycle belongs to the picker window,
/// not to per-screen view models.
/// </summary>
internal static class CommandNameLeak
{
    private static readonly string[] Banned = { "SwitchProject", "CloseProject", "SwitchProjectCommand", "CloseProjectCommand" };

    public static void Check(Type viewModelType)
    {
        var commands = viewModelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.EndsWith("Command", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToList();

        var leaked = commands.Where(name => Banned.Any(name.Contains)).ToList();
        leaked.Should().BeEmpty(
            $"{viewModelType.Name} must not expose a shell-level switch/close project command (found: {string.Join(", ", leaked)}).");
    }
}
