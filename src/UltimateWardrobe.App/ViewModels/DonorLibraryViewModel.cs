using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.ObjectModel;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Donor library screen (Phase 6 Sprint 6.3): a table of the open project's <see cref="DonorAsset"/>s
/// (Kind badge, ProvidedSets count, BodySlide/physics indicators, import date) plus the import drop
/// zone and the remove / reclassify / manual Kind override commands. Import runs the per-file Phase 2
/// pipeline on <see cref="IBackgroundTaskService"/> through <see cref="IDonorImportRunner"/> with a
/// per-file progress bar + cancel; a failed archive surfaces a typed dialog and adds nothing (the
/// Phase 2 guard already cleaned up). Every mutation autosaves through the shared <see cref="IProjectStore"/>.
/// </summary>
public sealed class DonorLibraryViewModel : ObservableObject
{
    private readonly IProjectSession _session;
    private readonly IBackgroundTaskService _backgroundTasks;
    private readonly IDonorImportRunner _importRunner;
    private readonly DonorLibraryService _donorService;
    private readonly IAppDialogService _dialogs;
    private readonly ILogger<DonorLibraryViewModel> _logger;
    private CancellationTokenSource? _importCts;
    private bool _isImporting;
    private bool _isOpen;
    private int _progressValue;
    private int _progressTotal;
    private string _progressText = string.Empty;
    private IAsyncRelayCommand<IEnumerable<string>>? _importCommand;
    private IAsyncRelayCommand<DonorRowViewModel>? _removeCommand;
    private IAsyncRelayCommand<DonorRowViewModel>? _reclassifyCommand;
    private IAsyncRelayCommand<DonorRowViewModel>? _setKindCommand;
    private IRelayCommand? _cancelCommand;

    public DonorLibraryViewModel(
        IProjectSession session,
        IBackgroundTaskService backgroundTasks,
        IDonorImportRunner importRunner,
        DonorLibraryService donorService,
        IAppDialogService dialogs,
        ILogger<DonorLibraryViewModel>? logger = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));
        _importRunner = importRunner ?? throw new ArgumentNullException(nameof(importRunner));
        _donorService = donorService ?? throw new ArgumentNullException(nameof(donorService));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _logger = logger ?? NullLogger<DonorLibraryViewModel>.Instance;
    }

    public ObservableCollection<DonorRowViewModel> Donors { get; } = new();

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public bool IsImporting
    {
        get => _isImporting;
        private set
        {
            if (SetProperty(ref _isImporting, value))
            {
                NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanImport => IsOpen && !IsImporting;

    public bool IsProgressVisible => IsImporting || ProgressTotal > 0;

    public int ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public int ProgressTotal
    {
        get => _progressTotal;
        private set
        {
            if (SetProperty(ref _progressTotal, value))
            {
                OnPropertyChanged(nameof(IsProgressVisible));
            }
        }
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public IAsyncRelayCommand<IEnumerable<string>> ImportCommand =>
        _importCommand ??= new AsyncRelayCommand<IEnumerable<string>>(ImportAsync, _ => CanImport);

    public IAsyncRelayCommand<DonorRowViewModel> RemoveCommand =>
        _removeCommand ??= new AsyncRelayCommand<DonorRowViewModel>(
            RemoveAsync, row => IsOpen && !IsImporting && row is not null);

    public IAsyncRelayCommand<DonorRowViewModel> ReclassifyCommand =>
        _reclassifyCommand ??= new AsyncRelayCommand<DonorRowViewModel>(
            ReclassifyAsync, row => IsOpen && !IsImporting && row is not null);

    public IAsyncRelayCommand<DonorRowViewModel> SetKindCommand =>
        _setKindCommand ??= new AsyncRelayCommand<DonorRowViewModel>(
            SetKindAsync, row => IsOpen && !IsImporting && row is not null);

    public IRelayCommand CancelCommand =>
        _cancelCommand ??= new RelayCommand(CancelImport, () => IsImporting);

    /// <summary>Rebuild the table from the open project's library and reconcile the open flag.</summary>
    public void Refresh()
    {
        IsOpen = _session.IsOpen;
        NotifyCanExecuteChanged();

        Donors.Clear();
        var project = _session.Project;
        if (project is null)
        {
            return;
        }

        foreach (var asset in project.Library.Assets)
        {
            Donors.Add(DonorPresentation.ToRow(asset));
        }

        OnPropertyChanged(nameof(IsProgressVisible));
    }

    /// <summary>
    /// Import a batch of dropped archive paths (the drop zone calls this; the command exposes it for
    /// <c>IEnumerable&lt;string&gt;</c>). Supported extensions are filtered up front; the runner
    /// re-filters as a guard. Runs on <see cref="IBackgroundTaskService"/> with per-file progress.
    /// </summary>
    private async Task ImportAsync(IEnumerable<string>? paths)
    {
        if (!IsOpen || IsImporting)
        {
            return;
        }

        var supported = paths?
            .Where(DonorImportRunner.IsSupportedArchive)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        if (supported.Count == 0)
        {
            return;
        }

        _importCts?.Dispose();
        _importCts = new CancellationTokenSource();
        var token = _importCts.Token;

        var project = _session.Project!;
        var snapshotCount = project.Library.Assets.Count;

        IsImporting = true;
        ProgressTotal = supported.Count;
        ProgressValue = 0;
        ProgressText = $"Importing {supported.Count} donor archive(s)...";

        try
        {
            var progress = new Progress<DonorImportProgress>(p =>
            {
                ProgressValue = p.FilesDone;
                ProgressTotal = p.TotalFiles;
                ProgressText = $"Importing {p.FilesDone} of {p.TotalFiles}...";
            });

            await _backgroundTasks.RunAsync(
                "Import donors",
                ct => _importRunner.ImportAsync(supported, project.RootPath, project.Library, BuildVanillaHint(project), progress, ct),
                token);

            ProgressValue = ProgressTotal;
            ProgressText = "Import finished.";
            _logger.LogInformation("Donor import completed for {Count} archive(s).", ProgressTotal);
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Import cancelled.";
            _logger.LogInformation("Donor import cancelled.");
        }
        catch (DonorAlreadyOwnedException ex)
        {
            ProgressText = "Import failed.";
            _logger.LogWarning(ex, "Donor import rejected an already-owned archive.");
            await _dialogs.AlertAsync("Archive already imported", ex.Message);
        }
        catch (Exception ex)
        {
            ProgressText = "Import failed.";
            _logger.LogError(ex, "Donor import failed.");
            await _dialogs.AlertAsync("Import failed", ex.Message);
        }
        finally
        {
            IsImporting = false;
            Refresh();
            if (project.Library.Assets.Count != snapshotCount)
            {
                await SaveAsync();
            }
        }
    }

    private async Task RemoveAsync(DonorRowViewModel? row)
    {
        if (row is null || !IsOpen || IsImporting)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Remove donor",
            $"Remove '{row.OriginalFileName}' from the library?\n\nThe extracted Source folder is deleted.");
        if (!confirmed)
        {
            return;
        }

        _donorService.RemoveAsync(_session.Project!.Library, row.ImportId);
        Refresh();
        _logger.LogInformation("Removed donor '{Name}'.", row.OriginalFileName);
        await SaveAsync();
    }

    private async Task ReclassifyAsync(DonorRowViewModel? row)
    {
        if (row is null || !IsOpen || IsImporting)
        {
            return;
        }

        IsImporting = true;
        try
        {
            await _backgroundTasks.RunAsync(
                "Reclassify donor",
                ct => _donorService.ReclassifyAsync(
                    _session.Project!.Library,
                    row.ImportId,
                    BuildVanillaHint(_session.Project),
                    ct));
            _logger.LogInformation("Reclassified donor '{Name}'.", row.OriginalFileName);
        }
        catch (OperationCanceledException)
        {
            // cancellation is silent
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reclassifying donor '{Name}' failed.", row.OriginalFileName);
            await _dialogs.AlertAsync("Reclassify failed", ex.Message);
        }
        finally
        {
            IsImporting = false;
            Refresh();
            await SaveAsync();
        }
    }

    private async Task SetKindAsync(DonorRowViewModel? row)
    {
        if (row is null || !IsOpen || IsImporting)
        {
            return;
        }

        var text = await _dialogs.PromptTextAsync(
            "Set donor kind",
            "Enter the kind (Full replacer, Body conversion patch, Physics patch):",
            row.KindText);
        var kind = DonorPresentation.ParseKind(text);
        if (kind is null || kind == row.Asset.Kind)
        {
            return;
        }

        OverrideKind(row, kind.Value);
        Refresh();
        _logger.LogInformation("Manually set kind for '{Name}' to {Kind}.", row.OriginalFileName, kind);
        await SaveAsync();
    }

    /// <summary>
    /// Manual Kind override (roadmap 4.3): <see cref="DonorAsset"/> is immutable, so the row's asset is
    /// rebuilt with the chosen <see cref="DonorAssetKind"/> and swapped in-place. Direct + testable.
    /// </summary>
    public void OverrideKind(DonorRowViewModel row, DonorAssetKind kind)
    {
        ArgumentNullException.ThrowIfNull(row);
        var asset = row.Asset;
        var updated = new DonorAsset(
            asset.ImportId,
            asset.OriginalFileName,
            asset.ExtractedPath,
            asset.ImportedAt,
            asset.ArchiveHash,
            kind,
            asset.ProvidedSets,
            asset.FileManifest,
            asset.DetectedBodySlideFiles,
            asset.DetectedPhysicsFiles);

        var library = _session.Project?.Library;
        if (library is null)
        {
            return;
        }

        var index = library.Assets.FindIndex(a => a.ImportId == asset.ImportId);
        if (index < 0)
        {
            return;
        }

        library.Assets[index] = updated;
    }

    private void CancelImport()
    {
        _importCts?.Cancel();
        ProgressText = "Cancelling...";
    }

    private void NotifyCanExecuteChanged()
    {
        OnPropertyChanged(nameof(CanImport));
        ImportCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ReclassifyCommand.NotifyCanExecuteChanged();
        SetKindCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveAsync()
    {
        var store = _session.Store;
        var project = _session.Project;
        if (store is null || project is null)
        {
            return;
        }

        await store.SaveAsync(project);
    }

    /// <summary>
    /// The project's vanilla hint for donor classification/reclassification (Sprint 6.3): the first
    /// Vanilla+DLC overhaul source root becomes a <see cref="VanillaCatalogSource"/>-backed Catalog so
    /// the Phase 2 classifier can merge reference game esms. Null when the project has no vanilla source.
    /// </summary>
    private static Catalog? BuildVanillaHint(Project project)
    {
        var source = project.Overhauls
            .Select(o => o.Source)
            .OfType<VanillaCatalogSource>()
            .FirstOrDefault();
        if (source is null)
        {
            return null;
        }

        return new Catalog(source, Array.Empty<ArmorSet>());
    }
}
