using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

using DonorLibraryModel = UltimateWardrobe.Core.Domain.DonorLibrary;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>A label + optional status filter value for the matrix header filter.</summary>
public sealed record StatusFilterOption(string Name, ArmorSetStatus? Value)
{
    public static StatusFilterOption All { get; } = new("All", null);

    public override string ToString() => Name;
}

/// <summary>
/// One status-legend entry (Sprint 6.6 polish, roadmap 8.5): a WPF-UI <see cref="Symbol"/> glyph plus
/// its text label, so statuses are rendered with symbols instead of emoji.
/// </summary>
public sealed record StatusLegendItem(string Symbol, string Label);

/// <summary>
/// Overhaul (mapping matrix) screen (Phase 6 Sprint 6.4, amendment 8): projects the current
/// Overhaul's <see cref="Overhaul.Catalog"/> into the FEMALE ARMOR / MALE ARMOR matrix - catalog sets
/// as row-band projections under gender sections, one column per weight class present in the catalog,
/// and one cell per (set, gender, weight) <see cref="Variant"/>. A mapped cell raises a card (set
/// name + one line per distinct base donor + one line per attached BodyConversion/Physics patch); a
/// missing variant or an unmapped variant renders blank. Search filters rows (both sections); a status
/// filter highlights rows/cells whose <see cref="MappingService.GetArmorSetStatus"/> matches. A null /
/// empty catalog shows the "run a scan first" empty state. Row activation feeds the anchored popover
/// (Sprint 6.5). Headless - only App-layer abstractions + <see cref="MappingService"/> are injected.
/// </summary>
public sealed class OverhaulViewModel : ObservableObject
{
    private readonly IProjectSession _session;
    private readonly IOverhaulSelection _selection;
    private readonly MappingService _mapping;
    private readonly ILogger<OverhaulViewModel> _logger;
    private readonly IAppNavigationService? _navigation;
    private readonly IAppDialogService? _dialogs;
    private readonly IDonorImportRunner? _importRunner;
    private readonly IBackgroundTaskService? _backgroundTasks;
    private ArmorSetDetailViewModel? _cellEditor;
    private string _searchText = string.Empty;
    private ArmorSetStatus? _statusFilter;
    private MatrixCellViewModel? _activeCell;
    private bool _isEditorOpen;
    private IRelayCommand<MatrixCellViewModel>? _activateCellCommand;
    private CancellationTokenSource? _filterCts;
    private int _filterGeneration;
    private string _progressLabel = string.Empty;

    public OverhaulViewModel(
        IProjectSession session,
        IOverhaulSelection selection,
        MappingService mapping,
        IAppNavigationService? navigation = null,
        IAppDialogService? dialogs = null,
        IDonorImportRunner? importRunner = null,
        IBackgroundTaskService? backgroundTasks = null,
        ILogger<OverhaulViewModel>? logger = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _backgroundTasks = backgroundTasks;
        _navigation = navigation;
        _dialogs = dialogs;
        _importRunner = importRunner;
        _logger = logger ?? NullLogger<OverhaulViewModel>.Instance;
    }

    public string OverhaulName => Current?.Name ?? "No overhaul";

    public bool HasCatalog => Current?.Catalog is { Sets.Count: > 0 };

    public bool IsEmpty => !HasCatalog;

    public string EmptyMessage => "No catalog - run a scan first";

    public IReadOnlyList<MatrixColumnViewModel> Columns { get; private set; } = Array.Empty<MatrixColumnViewModel>();

    public IReadOnlyList<MatrixSectionViewModel> Sections { get; private set; } = Array.Empty<MatrixSectionViewModel>();

    /// <summary>
    /// Flat, virtualizable projection of the matrix (Sprint 6.8, manual-testing bug 3): section
    /// headers and row bands in order, bound to one <c>VirtualizingStackPanel</c>-backed
    /// <c>ItemsControl</c> so a large catalog only realizes on-screen rows.
    /// </summary>
    public IReadOnlyList<MatrixItemViewModel> MatrixItems { get; private set; } = Array.Empty<MatrixItemViewModel>();

    public string ProgressLabel
    {
        get => _progressLabel;
        private set => SetProperty(ref _progressLabel, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RequestFilter();
            }
        }
    }

    public ArmorSetStatus? StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetProperty(ref _statusFilter, value))
            {
                RequestFilter();
            }
        }
    }

    /// <summary>Selectable status-filter entries (All + each status) for the header filter.</summary>
    public static IReadOnlyList<StatusFilterOption> StatusOptions => new[]
    {
        new StatusFilterOption("All", null),
        new StatusFilterOption("NotStarted", ArmorSetStatus.NotStarted),
        new StatusFilterOption("InProgress", ArmorSetStatus.InProgress),
        new StatusFilterOption("Mapped", ArmorSetStatus.Mapped),
        new StatusFilterOption("NeedsPatch", ArmorSetStatus.NeedsPatch),
        new StatusFilterOption("Done", ArmorSetStatus.Done),
    };

    /// <summary>
    /// Status-legend glyphs (Sprint 6.6 polish, roadmap 8.5): a WPF-UI symbol + label + accent for each
    /// matrix status, rendered as a text legend instead of emoji.
    /// </summary>
    public static IReadOnlyList<StatusLegendItem> StatusLegend => new[]
    {
        new StatusLegendItem("CheckmarkCircle24", "Done"),
        new StatusLegendItem("GridDots24", "Mapped"),
        new StatusLegendItem("Warning24", "NeedsPatch"),
        new StatusLegendItem("Clock24", "InProgress"),
        new StatusLegendItem("Circle20", "NotStarted"),
    };

    private StatusFilterOption _selectedStatusOption = StatusFilterOption.All;
    public StatusFilterOption SelectedStatusOption
    {
        get => _selectedStatusOption;
        set
        {
            if (SetProperty(ref _selectedStatusOption, value))
            {
                StatusFilter = value.Value;
            }
        }
    }

    public MatrixCellViewModel? ActiveCell
    {
        get => _activeCell;
        private set => SetProperty(ref _activeCell, value);
    }

    /// <summary>True while the anchored popover editor is open (Phase 6 Sprint 6.5).</summary>
    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        private set => SetProperty(ref _isEditorOpen, value);
    }

    /// <summary>
    /// The one shared single-cell editor (Phase 6 Sprint 6.5). Created lazily and reused across cells;
    /// it is bound to the activated cell's variant on open and cleared on close.
    /// </summary>
    public ArmorSetDetailViewModel CellEditor
    {
        get
        {
            if (_cellEditor is null)
            {
                _cellEditor = new ArmorSetDetailViewModel(_mapping, _navigation, _dialogs, _importRunner);
                _cellEditor.Changed += OnCellEdited;
            }

            return _cellEditor;
        }
    }

    public IRelayCommand<MatrixCellViewModel> ActivateCellCommand =>
        _activateCellCommand ??= new RelayCommand<MatrixCellViewModel>(cell =>
        {
            if (cell is null) return;
            if (IsEditorOpen && ReferenceEquals(ActiveCell, cell))
            {
                CloseEditor();
            }
            else
            {
                Activate(cell);
            }
        }, cell => cell is not null);

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

    /// <summary>Rebuilds the matrix and clears the popover editor (call on page Loaded and after edits).</summary>
    public void Refresh()
    {
        ClearEditorState();
        RecomputeMatrix();
    }

    /// <summary>Re-projects Columns/Sections without touching the active editor (used after a cell edit).</summary>
    private void RecomputeMatrix()
    {
        var overhaul = Current;
        var catalog = overhaul?.Catalog;
        if (overhaul is null || catalog is null)
        {
            Columns = Array.Empty<MatrixColumnViewModel>();
            Sections = Array.Empty<MatrixSectionViewModel>();
            MatrixItems = Array.Empty<MatrixItemViewModel>();
            ProgressLabel = string.Empty;
            RaiseMatrixChanged();
            return;
        }

        var matrix = OverhaulMatrix.Build(
            catalog, overhaul.Mappings, _session.Project!.Library, _mapping, _searchText, _statusFilter);
        var progress = _mapping.GetOverhaulProgress(overhaul.Mappings, catalog);
        var progressLabel = $"{progress.Done} done / {progress.Mapped} mapped / {progress.NeedsPatch} need patch / {progress.TotalSets} total";
        ApplyMatrixResult(matrix, progressLabel);
    }

    private void RequestFilter()
    {
        if (_backgroundTasks is null)
        {
            RecomputeMatrix();
            return;
        }

        ScheduleFilterAsync();
    }

    private void ScheduleFilterAsync()
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;
        var generation = Interlocked.Increment(ref _filterGeneration);

        var overhaul = Current;
        var catalog = overhaul?.Catalog;
        if (overhaul is null || catalog is null)
        {
            Columns = Array.Empty<MatrixColumnViewModel>();
            Sections = Array.Empty<MatrixSectionViewModel>();
            MatrixItems = Array.Empty<MatrixItemViewModel>();
            ProgressLabel = string.Empty;
            RaiseMatrixChanged();
            return;
        }

        var searchSnapshot = _searchText;
        var statusSnapshot = _statusFilter;
        var mappingsSnapshot = overhaul.Mappings.ToList();
        var library = _session.Project!.Library;
        var libraryCopy = new DonorLibraryModel(library.ProjectId);
        foreach (var asset in library.Assets)
        {
            libraryCopy.Assets.Add(asset);
        }

        _ = FilterAsync(catalog, mappingsSnapshot, libraryCopy, searchSnapshot, statusSnapshot, generation, token);
    }

    private async Task FilterAsync(
        Catalog catalog,
        List<PieceMapping> mappingsSnapshot,
        DonorLibraryModel libraryCopy,
        string searchSnapshot,
        ArmorSetStatus? statusSnapshot,
        int generation,
        CancellationToken token)
    {
        OverhaulMatrixViewModel? matrix = null;
        string progressLabel = string.Empty;
        try
        {
            await _backgroundTasks!.RunAsync("Filter matrix", ct =>
            {
                ct.ThrowIfCancellationRequested();
                matrix = OverhaulMatrix.Build(catalog, mappingsSnapshot, libraryCopy, _mapping, searchSnapshot, statusSnapshot);
                var progress = _mapping.GetOverhaulProgress(mappingsSnapshot, catalog);
                progressLabel = $"{progress.Done} done / {progress.Mapped} mapped / {progress.NeedsPatch} need patch / {progress.TotalSets} total";
                return Task.CompletedTask;
            }, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Filter matrix failed.");
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        if (generation != Volatile.Read(ref _filterGeneration))
        {
            return;
        }

        if (matrix is null)
        {
            Sections = Array.Empty<MatrixSectionViewModel>();
            MatrixItems = Array.Empty<MatrixItemViewModel>();
            ProgressLabel = progressLabel;
            RaiseMatrixChangedWithoutColumns();
            return;
        }

        ApplyMatrixResult(matrix, progressLabel);
    }

    private void ApplyMatrixResult(OverhaulMatrixViewModel matrix, string progressLabel)
    {
        var columnsChanged = !AreColumnsEqual(Columns, matrix.Columns);
        if (columnsChanged)
        {
            Columns = matrix.Columns;
        }

        Sections = matrix.Sections;
        MatrixItems = matrix.MatrixItems;
        ProgressLabel = progressLabel;
        if (columnsChanged)
        {
            RaiseMatrixChanged();
        }
        else
        {
            RaiseMatrixChangedWithoutColumns();
        }
    }

    private static bool AreColumnsEqual(IReadOnlyList<MatrixColumnViewModel> a, IReadOnlyList<MatrixColumnViewModel> b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Weight != b[i].Weight)
            {
                return false;
            }
        }

        return true;
    }

    private void RaiseMatrixChanged()
    {
        OnPropertyChanged(nameof(OverhaulName));
        OnPropertyChanged(nameof(HasCatalog));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(MatrixItems));
        OnPropertyChanged(nameof(ProgressLabel));
    }

    private void RaiseMatrixChangedWithoutColumns()
    {
        OnPropertyChanged(nameof(OverhaulName));
        OnPropertyChanged(nameof(HasCatalog));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(MatrixItems));
        OnPropertyChanged(nameof(ProgressLabel));
    }

    /// <summary>
    /// Resolves the cell at a matrix coordinate (Phase 6 Sprint 6.4): section index (0 = FEMALE,
    /// 1 = MALE as emitted), row index within that section, column index. Returns null out of range.
    /// The anchored popover (Sprint 6.5) pins to this cell's bounds.
    /// </summary>
    public MatrixCellViewModel? CellAt(int sectionIndex, int rowIndex, int columnIndex)
    {
        var section = sectionIndex >= 0 && sectionIndex < Sections.Count ? Sections[sectionIndex] : null;
        var row = section is not null && rowIndex >= 0 && rowIndex < section.Rows.Count ? section.Rows[rowIndex] : null;
        var cell = row is not null && columnIndex >= 0 && columnIndex < row.Cells.Count ? row.Cells[columnIndex] : null;
        return cell;
    }

    /// <summary>Activates (opens the popover editor for) a cell - feeds the anchored popover anchor (Sprint 6.5).</summary>
    public void Activate(MatrixCellViewModel? cell)
    {
        if (cell is null)
        {
            throw new ArgumentNullException(nameof(cell));
        }

        if (IsEditorOpen && ReferenceEquals(ActiveCell, cell))
        {
            CloseEditor();
            return;
        }

        var catalog = Current?.Catalog;
        var overhaul = Current;
        if (catalog is null || overhaul is null)
        {
            return;
        }

        // Resolve the (set, gender, weight) variant directly from the set - the matrix cell itself is
        // blank (Variant null) for an unmapped variant, yet the editor must still open to assign the
        // first donor. Reuse the matrix's lookup semantics.
        var variant = cell.Set.Variants.FirstOrDefault(v =>
            v.Weight == cell.Weight
            && (v.Gender == cell.SectionGender || v.Gender == Gender.Unisex));
        if (variant is null)
        {
            // No variant for this (set, gender, weight) cell - nothing to edit.
            return;
        }

        _logger.LogInformation("Opening popover editor for '{Set}' {Gender} {Weight}.", cell.Set.DisplayName, cell.SectionGender, cell.Weight);
        CellEditor.Open(cell.Set, variant, overhaul, _session.Project!.Library, _session.Project);
        ActiveCell = cell;
        IsEditorOpen = true;
    }

    /// <summary>Closes the anchored popover editor, flushing the pending autosave.</summary>
    public void CloseEditor()
    {
        _ = FlushAndCloseEditorAsync();
    }

    /// <summary>Flushes the autosave (awaited) then clears the editor - the guaranteed close flush (Sprint 6.5).</summary>
    public async Task FlushAndCloseEditorAsync()
    {
        await AutosaveAsync();
        ClearEditorState();
    }

    private void ClearEditorState()
    {
        if (IsEditorOpen)
        {
            CellEditor.Close();
        }

        ActiveCell = null;
        IsEditorOpen = false;
    }

    private void OnCellEdited(object? sender, EventArgs e)
    {
        // Re-project the matrix after an edit so the grid never diverges; keep the editor open.
        // Use async path when background service is available to avoid blocking UI on large catalogs.
        if (_backgroundTasks is null)
        {
            RecomputeMatrixPreserveEditor();
        }
        else
        {
            ScheduleFilterPreserveEditorAsync();
        }

        _ = AutosaveAsync();
    }

    private void RecomputeMatrixPreserveEditor()
    {
        var overhaul = Current;
        var catalog = overhaul?.Catalog;
        if (overhaul is null || catalog is null)
        {
            Columns = Array.Empty<MatrixColumnViewModel>();
            Sections = Array.Empty<MatrixSectionViewModel>();
            MatrixItems = Array.Empty<MatrixItemViewModel>();
            ProgressLabel = string.Empty;
            RaiseMatrixChanged();
            return;
        }

        var matrix = OverhaulMatrix.Build(
            catalog, overhaul.Mappings, _session.Project!.Library, _mapping, _searchText, _statusFilter);
        var progress = _mapping.GetOverhaulProgress(overhaul.Mappings, catalog);
        var progressLabel = $"{progress.Done} done / {progress.Mapped} mapped / {progress.NeedsPatch} need patch / {progress.TotalSets} total";
        ApplyMatrixResult(matrix, progressLabel);
    }

    private void ScheduleFilterPreserveEditorAsync()
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;
        var generation = Interlocked.Increment(ref _filterGeneration);

        var overhaul = Current;
        var catalog = overhaul?.Catalog;
        if (overhaul is null || catalog is null)
        {
            Columns = Array.Empty<MatrixColumnViewModel>();
            Sections = Array.Empty<MatrixSectionViewModel>();
            MatrixItems = Array.Empty<MatrixItemViewModel>();
            ProgressLabel = string.Empty;
            RaiseMatrixChanged();
            return;
        }

        var searchSnapshot = _searchText;
        var statusSnapshot = _statusFilter;
        var mappingsSnapshot = overhaul.Mappings.ToList();
        var library = _session.Project!.Library;
        var libraryCopy = new DonorLibraryModel(library.ProjectId);
        foreach (var asset in library.Assets)
        {
            libraryCopy.Assets.Add(asset);
        }

        _ = FilterPreserveEditorAsync(catalog, mappingsSnapshot, libraryCopy, searchSnapshot, statusSnapshot, generation, token);
    }

    private async Task FilterPreserveEditorAsync(
        Catalog catalog,
        List<PieceMapping> mappingsSnapshot,
        DonorLibraryModel libraryCopy,
        string searchSnapshot,
        ArmorSetStatus? statusSnapshot,
        int generation,
        CancellationToken token)
    {
        OverhaulMatrixViewModel? matrix = null;
        string progressLabel = string.Empty;
        try
        {
            await _backgroundTasks!.RunAsync("Filter matrix (preserve editor)", ct =>
            {
                ct.ThrowIfCancellationRequested();
                matrix = OverhaulMatrix.Build(catalog, mappingsSnapshot, libraryCopy, _mapping, searchSnapshot, statusSnapshot);
                var progress = _mapping.GetOverhaulProgress(mappingsSnapshot, catalog);
                progressLabel = $"{progress.Done} done / {progress.Mapped} mapped / {progress.NeedsPatch} need patch / {progress.TotalSets} total";
                return Task.CompletedTask;
            }, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Filter matrix (preserve editor) failed.");
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        if (generation != Volatile.Read(ref _filterGeneration))
        {
            return;
        }

        if (matrix is null)
        {
            Sections = Array.Empty<MatrixSectionViewModel>();
            MatrixItems = Array.Empty<MatrixItemViewModel>();
            ProgressLabel = progressLabel;
            RaiseMatrixChangedWithoutColumns();
            return;
        }

        ApplyMatrixResult(matrix, progressLabel);
    }

    private async Task AutosaveAsync()
    {
        try
        {
            if (_session.IsOpen && _session.Store is not null && _session.Project is not null)
            {
                await _session.Store.SaveAsync(_session.Project);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Autosave failed.");
        }
    }
}
