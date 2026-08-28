using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Export screen (Phase 6 Sprint 6.6, roadmap tasks 6.6-6.8): a pre-export checklist with a
/// per-set status rollup, an "allow partial" switch, an output-folder field (defaulting to
/// <c>&lt;Project.Root&gt;\Export</c>), and the "build wardrobe" invocation of <see cref="IPatcher"/>
/// through <see cref="IBackgroundTaskService"/>. The build reports <see cref="PatchProgress"/> stage
/// updates and is cancellable, then renders a result card from <see cref="PatchResult"/>/<see cref="PatchReport"/>
/// (mod folder, overridden records, copied files/bytes, warnings) with open-in-Explorer and re-export
/// (the patcher clears the mod folder before writing, so re-running is a clean rebuild).
/// </summary>
public sealed class ExportViewModel : ObservableObject
{
    private readonly IProjectSession _session;
    private readonly IOverhaulSelection _selection;
    private readonly MappingService _mapping;
    private readonly IPatcher _patcher;
    private readonly IBackgroundTaskService _backgroundTasks;
    private readonly ISnackbarService _snackbar;
    private readonly IAppDialogService? _dialogs;
    private readonly ILogger<ExportViewModel> _logger;

    private string _outputFolder = string.Empty;
    private bool _allowPartial;
    private bool _isBuilding;
    private CancellationTokenSource? _buildCts;

    private int _totalSets;
    private int _setsDone;
    private int _setsNeedsPatch;
    private int _setsInProgress;
    private int _setsNotStarted;
    private int _setsReady;

    private string? _currentStage;
    private int _completedStages;
    private int _totalStages;
    private string? _progressDetail;

    private bool _isResultVisible;
    private string? _resultPluginPath;
    private string? _resultModFolder;
    private int _overriddenRecords;
    private int _copiedFilesCount;
    private string _copiedBytesText = "0 B";
    private bool _hasWarnings;

    private IReadOnlyList<PatchWarning> _resultWarnings = new List<PatchWarning>();
    private bool _isEmpty = true;
    private string? _emptyMessage;
    private string? _overhaulName;

    public ExportViewModel(
        IProjectSession session,
        IOverhaulSelection selection,
        MappingService mapping,
        IPatcher patcher,
        IBackgroundTaskService backgroundTasks,
        ISnackbarService snackbar,
        IAppDialogService? dialogs = null,
        ILogger<ExportViewModel>? logger = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _patcher = patcher ?? throw new ArgumentNullException(nameof(patcher));
        _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));
        _snackbar = snackbar ?? throw new ArgumentNullException(nameof(snackbar));
        _dialogs = dialogs;
        _logger = logger ?? NullLogger<ExportViewModel>.Instance;

        BuildCommand = new AsyncRelayCommand(BuildAsync, CanBuild);
        CancelBuildCommand = new RelayCommand(CancelBuild, () => IsBuilding);
        OpenInExplorerCommand = new RelayCommand(OpenInExplorer, () => IsResultVisible && _resultModFolder is not null);
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    public string? EmptyMessage
    {
        get => _emptyMessage;
        private set => SetProperty(ref _emptyMessage, value);
    }

    public string? OverhaulName
    {
        get => _overhaulName;
        private set => SetProperty(ref _overhaulName, value);
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetProperty(ref _outputFolder, value))
            {
                BuildCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool AllowPartial
    {
        get => _allowPartial;
        set
        {
            if (SetProperty(ref _allowPartial, value))
            {
                AllowPartialHint = value ? null : "Partial builds are disabled: all armor sets must be ready.";
                BuildCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string? _allowPartialHint;
    public string? AllowPartialHint
    {
        get => _allowPartialHint;
        private set => SetProperty(ref _allowPartialHint, value);
    }

    // Pre-export checklist (per-set status rollup, roadmap 6.6). "Ready" is the mapped-but-not-done
    // set bucket - the sets that can actually be exported.
    public int TotalSets => _totalSets;
    public int SetsDone => _setsDone;
    public int SetsReady => _setsReady;
    public int SetsNeedsPatch => _setsNeedsPatch;
    public int SetsInProgress => _setsInProgress;
    public int SetsNotStarted => _setsNotStarted;

    public bool IsBuilding
    {
        get => _isBuilding;
        private set
        {
            if (SetProperty(ref _isBuilding, value))
            {
                CancelBuildCommand.NotifyCanExecuteChanged();
                BuildCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Build button label: "Re-export" once a build has completed, otherwise "Собрать гардероб".</summary>
    public string BuildButtonLabel => IsResultVisible ? "Re-export" : "Собрать гардероб";

    // Progress (PatchProgress stage rollup).
    public string? CurrentStage
    {
        get => _currentStage;
        private set => SetProperty(ref _currentStage, value);
    }

    public int CompletedStages
    {
        get => _completedStages;
        private set => SetProperty(ref _completedStages, value);
    }

    public int TotalStages
    {
        get => _totalStages;
        private set => SetProperty(ref _totalStages, value);
    }

    public string? ProgressDetail
    {
        get => _progressDetail;
        private set => SetProperty(ref _progressDetail, value);
    }

    public double ProgressPercent =>
        TotalStages == 0 ? 0d : Math.Round((double)CompletedStages / TotalStages * 100, 0);

    // Result card.
    public bool IsResultVisible
    {
        get => _isResultVisible;
        private set
        {
            if (SetProperty(ref _isResultVisible, value))
            {
                OnPropertyChanged(nameof(BuildButtonLabel));
            }
        }
    }

    public string? ResultPluginPath
    {
        get => _resultPluginPath;
        private set => SetProperty(ref _resultPluginPath, value);
    }

    public string? ResultModFolder
    {
        get => _resultModFolder;
        private set => SetProperty(ref _resultModFolder, value);
    }

    public int OverriddenRecords
    {
        get => _overriddenRecords;
        private set => SetProperty(ref _overriddenRecords, value);
    }

    public int CopiedFilesCount
    {
        get => _copiedFilesCount;
        private set => SetProperty(ref _copiedFilesCount, value);
    }

    public string CopiedBytesText
    {
        get => _copiedBytesText;
        private set => SetProperty(ref _copiedBytesText, value);
    }

    public bool HasWarnings
    {
        get => _hasWarnings;
        private set => SetProperty(ref _hasWarnings, value);
    }

    public IReadOnlyList<PatchWarning> ResultWarnings
    {
        get => _resultWarnings;
        private set
        {
            if (SetProperty(ref _resultWarnings, value))
            {
                HasWarnings = value.Count > 0;
            }
        }
    }

    public IAsyncRelayCommand BuildCommand { get; }
    public IRelayCommand CancelBuildCommand { get; }
    public IRelayCommand OpenInExplorerCommand { get; }

    private Overhaul? Current
    {
        get
        {
            if (!_session.IsOpen || _session.Project is null || !_selection.OverhaulId.HasValue)
            {
                return null;
            }

            return _session.Project.Overhauls.FirstOrDefault(o => o.Id == _selection.OverhaulId.Value);
        }
    }

    /// <summary>Recomputes the checklist from the current overhaul; clears the result card.</summary>
    public void Refresh()
    {
        var current = Current;
        if (current is null || current.Catalog is null)
        {
            IsEmpty = true;
            EmptyMessage =
                current is null
                    ? "Open a project and select an overhaul (Overhaul screen) before exporting."
                    : "This overhaul has no catalog scan yet. Run a scan on the Overhaul screen first.";
            OverhaulName = current?.Name;
            ClearChecklist();
            IsResultVisible = false;
            return;
        }

        IsEmpty = false;
        EmptyMessage = null;
        OverhaulName = current.Name;

        if (string.IsNullOrWhiteSpace(OutputFolder) && _session.Project is not null)
        {
            OutputFolder = Path.Combine(_session.Project.RootPath, "Export");
        }

        var progress = _mapping.GetOverhaulProgress(current.Mappings, current.Catalog);
        _totalSets = progress.TotalSets;
        _setsDone = progress.Done;
        _setsReady = progress.Mapped;
        _setsNeedsPatch = progress.NeedsPatch;
        _setsInProgress = progress.InProgress;
        _setsNotStarted = progress.NotStarted;
        OnPropertyChanged(nameof(TotalSets));
        OnPropertyChanged(nameof(SetsDone));
        OnPropertyChanged(nameof(SetsReady));
        OnPropertyChanged(nameof(SetsNeedsPatch));
        OnPropertyChanged(nameof(SetsInProgress));
        OnPropertyChanged(nameof(SetsNotStarted));
        BuildCommand.NotifyCanExecuteChanged();

        IsResultVisible = false;
    }

    private bool CanBuild() => !IsBuilding && !IsEmpty && !string.IsNullOrWhiteSpace(OutputFolder) && HasAnythingToExport();

    private bool HasAnythingToExport()
    {
        // A full build needs every set ready unless partial builds are allowed.
        if (AllowPartial)
        {
            return TotalSets > 0;
        }

        return TotalSets > 0 && IsCompletelyReady();
    }

    private bool IsCompletelyReady() => SetsNotStarted == 0 && SetsInProgress == 0 && SetsNeedsPatch == 0;

    private async Task BuildAsync()
    {
        var current = Current;
        if (current is null || current.Catalog is null || _session.Project is null)
        {
            return;
        }

        if (!AllowPartial && !IsCompletelyReady())
        {
            _snackbar.Show("Export blocked", "Not all armor sets are ready. Enable partial export or finish the missing sets.");
            return;
        }

        var outputFolder = string.IsNullOrWhiteSpace(OutputFolder)
            ? Path.Combine(_session.Project.RootPath, "Export")
            : OutputFolder;

        using var cts = new CancellationTokenSource();
        _buildCts = cts;
        IsBuilding = true;
        IsResultVisible = false;
        ResetProgress();

        _logger.LogInformation("Building wardrobe for '{Name}' into '{Dir}'.", current.Name, outputFolder);
        try
        {
            PatchResult result;
            try
            {
                var progress = new Progress<PatchProgress>(p => ApplyProgress(p));
                PatchResult? built = null;
                await _backgroundTasks.RunAsync("Build wardrobe export", async ct =>
                {
                    built = await _patcher.BuildAsync(current, _session.Project!.Library, outputFolder, progress, ct);
                }, cts.Token);
                result = built ?? throw new InvalidOperationException("Patcher returned no result.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Wardrobe export for '{Name}' was cancelled.", current.Name);
                _snackbar.Show("Export cancelled", "Wardrobe export was cancelled.");
                return;
            }

            IsResultVisible = true;
            ResultPluginPath = result.PluginPath;
            ResultModFolder = result.CopiedFiles.Count > 0 ? Path.GetDirectoryName(result.PluginPath) : null;
            OverriddenRecords = result.Report?.OverriddenRecords ?? 0;
            CopiedFilesCount = result.CopiedFiles.Count;
            CopiedBytesText = FormatBytes(result.Report?.CopiedBytes ?? 0);
            ResultWarnings = result.Report?.Warnings ?? new List<PatchWarning>();
            CompletedStages = TotalStages > 0 ? TotalStages : CompletedStages;
            _logger.LogInformation("Export complete for '{Name}' -> '{Plugin}'.", current.Name, result.PluginPath);
            _snackbar.Show("Export complete", "The wardrobe was exported successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Wardrobe export for '{Name}' failed.", current.Name);
            var message = ex.Message;
            if (_dialogs is not null)
            {
                await _dialogs.AlertAsync("Export failed", message);
            }
            else
            {
                _snackbar.Show("Export failed", message);
            }
        }
        finally
        {
            _buildCts = null;
            IsBuilding = false;
            OpenInExplorerCommand.NotifyCanExecuteChanged();
        }
    }

    private void CancelBuild()
    {
        _buildCts?.Cancel();
    }

    private void ResetProgress()
    {
        CurrentStage = null;
        CompletedStages = 0;
        TotalStages = 0;
        ProgressDetail = null;
        CurrentStage = "Starting";
    }

    private void ApplyProgress(PatchProgress p)
    {
        CurrentStage = p.Stage;
        TotalStages = Math.Max(TotalStages, p.Total);
        CompletedStages = Math.Max(CompletedStages, p.Completed);
        ProgressDetail = p.Detail;
        OnPropertyChanged(nameof(ProgressPercent));
        _logger.LogInformation("Export stage '{Stage}' {Completed}/{Total}.", p.Stage, p.Completed, p.Total);
    }

    private void OpenInExplorer()
    {
        if (ResultModFolder is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(ResultModFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open the export folder '{Folder}'.", ResultModFolder);
            _snackbar.Show("Open failed", ex.Message);
        }
    }

    private void ClearChecklist()
    {
        _totalSets = 0;
        _setsDone = 0;
        _setsReady = 0;
        _setsNeedsPatch = 0;
        _setsInProgress = 0;
        _setsNotStarted = 0;
        OnPropertyChanged(nameof(TotalSets));
        OnPropertyChanged(nameof(SetsDone));
        OnPropertyChanged(nameof(SetsReady));
        OnPropertyChanged(nameof(SetsNeedsPatch));
        OnPropertyChanged(nameof(SetsInProgress));
        OnPropertyChanged(nameof(SetsNotStarted));
        BuildCommand.NotifyCanExecuteChanged();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }
}
