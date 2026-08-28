using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>A label + optional status filter value for the matrix header filter.</summary>
public sealed record StatusFilterOption(string Name, ArmorSetStatus? Value)
{
    public static StatusFilterOption All { get; } = new("All", null);

    public override string ToString() => Name;
}

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
    private string _searchText = string.Empty;
    private ArmorSetStatus? _statusFilter;
    private MatrixCellViewModel? _activeCell;
    private IRelayCommand<MatrixCellViewModel>? _activateCellCommand;

    public OverhaulViewModel(
        IProjectSession session,
        IOverhaulSelection selection,
        MappingService mapping,
        ILogger<OverhaulViewModel>? logger = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _logger = logger ?? NullLogger<OverhaulViewModel>.Instance;
    }

    public string OverhaulName => Current?.Name ?? "No overhaul";

    public bool HasCatalog => Current?.Catalog is { Sets.Count: > 0 };

    public bool IsEmpty => !HasCatalog;

    public string EmptyMessage => "No catalog - run a scan first";

    public IReadOnlyList<MatrixColumnViewModel> Columns { get; private set; } = Array.Empty<MatrixColumnViewModel>();

    public IReadOnlyList<MatrixSectionViewModel> Sections { get; private set; } = Array.Empty<MatrixSectionViewModel>();

    public string ProgressLabel
    {
        get
        {
            var overhaul = Current;
            var catalog = overhaul?.Catalog;
            if (catalog is null || overhaul is null)
            {
                return string.Empty;
            }

            var progress = _mapping.GetOverhaulProgress(overhaul.Mappings, catalog);
            return $"{progress.Done} done / {progress.Mapped} mapped / {progress.NeedsPatch} need patch / {progress.TotalSets} total";
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                Refresh();
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
                Refresh();
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

    public IRelayCommand<MatrixCellViewModel> ActivateCellCommand =>
        _activateCellCommand ??= new RelayCommand<MatrixCellViewModel>(Activate, cell => cell is not null);

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

    /// <summary>Rebuilds the matrix from the current overhaul (call on page Loaded and after edits).</summary>
    public void Refresh()
    {
        ActiveCell = null;
        var overhaul = Current;
        var catalog = overhaul?.Catalog;
        if (overhaul is null || catalog is null)
        {
            Columns = Array.Empty<MatrixColumnViewModel>();
            Sections = Array.Empty<MatrixSectionViewModel>();
            OnPropertyChanged(nameof(OverhaulName));
            OnPropertyChanged(nameof(HasCatalog));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Columns));
            OnPropertyChanged(nameof(Sections));
            OnPropertyChanged(nameof(ProgressLabel));
            return;
        }

        var matrix = OverhaulMatrix.Build(
            catalog, overhaul.Mappings, _session.Project!.Library, _mapping, _searchText, _statusFilter);
        Columns = matrix.Columns;
        Sections = matrix.Sections;

        OnPropertyChanged(nameof(OverhaulName));
        OnPropertyChanged(nameof(HasCatalog));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Sections));
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

    /// <summary>Activates a cell - feeds the anchored popover anchor (Sprint 6.5).</summary>
    public void Activate(MatrixCellViewModel? cell)
    {
        if (cell is null)
        {
            throw new ArgumentNullException(nameof(cell));
        }

        _logger.LogInformation("Activated matrix cell '{Set}' {Gender} {Weight}.", cell.Set.DisplayName, cell.SectionGender, cell.Weight);
        ActiveCell = cell;
    }
}
