using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Views;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.App.ViewModels;

using DonorLibraryModel = UltimateWardrobe.Core.Domain.DonorLibrary;

/// <summary>
/// Single-cell mapping editor (Phase 6 Sprint 6.5, amendment 8): the rescoped
/// <see cref="ArmorSetDetailViewModel"/>. A transient editor instance is bound to exactly one
/// (set, gender, weight) <see cref="Variant"/> of the owning <see cref="Overhaul"/> and its
/// <see cref="PieceMapping"/>s; it is created per activated cell and disposed (cleared) when the
/// anchored popover closes. The editor publishes <see cref="Changed"/> after every edit so the host
/// re-projects the matrix and flushes the autosave - the grid never holds divergent state.
/// The Phase 3 mapping command set (<see cref="AssignDonor"/>, <see cref="AttachBodyPatch"/>,
/// <see cref="AttachPhysicsPatch"/>, <see cref="Unassign"/>, <see cref="DetachBodyPatch"/>,
/// <see cref="DetachPhysicsPatch"/>, <see cref="SetNotes"/>) is kept verbatim over
/// <see cref="MappingService"/>. Headless - only <see cref="MappingService"/> + UI abstractions are
/// injected.
/// </summary>
public sealed class ArmorSetDetailViewModel : ObservableObject
{
    private readonly MappingService _mapping;
    private readonly IAppNavigationService? _navigation;
    private readonly IAppDialogService? _dialogs;
    private readonly ILogger<ArmorSetDetailViewModel> _logger;

    private Overhaul? _overhaul;
    private ArmorSet? _set;
    private Variant? _variant;
    private DonorLibraryModel? _library;
    private IReadOnlyList<PieceEditRowViewModel> _rows = Array.Empty<PieceEditRowViewModel>();

    private IRelayCommand<PieceEditRowViewModel>? _assignDonorCommand;
    private IRelayCommand<PieceEditRowViewModel>? _attachBodyPatchCommand;
    private IRelayCommand<PieceEditRowViewModel>? _attachPhysicsPatchCommand;
    private IRelayCommand<PieceEditRowViewModel>? _unassignCommand;
    private IRelayCommand<PieceEditRowViewModel>? _detachBodyPatchCommand;
    private IRelayCommand<PieceEditRowViewModel>? _detachPhysicsPatchCommand;
    private IRelayCommand<PieceEditRowViewModel>? _setNotesCommand;
    private IRelayCommand? _importPatchCommand;

    public ArmorSetDetailViewModel(
        MappingService mapping,
        IAppNavigationService? navigation = null,
        IAppDialogService? dialogs = null,
        ILogger<ArmorSetDetailViewModel>? logger = null)
    {
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _navigation = navigation;
        _dialogs = dialogs;
        _logger = logger ?? NullLogger<ArmorSetDetailViewModel>.Instance;
    }

    /// <summary>Raised after every mutation so the host re-projects the matrix and flushes autosave.</summary>
    public event EventHandler? Changed;

    public bool IsOpen => _overhaul is not null;

    /// <summary>The bound armor set while the editor is open (null after close).</summary>
    public ArmorSet? Set => _set;

    /// <summary>The bound (set, gender, weight) variant while the editor is open (null after close).</summary>
    public Variant? Variant => _variant;

    public string Title
    {
        get
        {
            if (_set is null || _variant is null) return "Cell editor";
            return $"{_set.DisplayName} - {_variant.Gender} {_variant.Weight}";
        }
    }

    public IReadOnlyList<PieceEditRowViewModel> Rows
    {
        get => _rows;
        private set
        {
            _rows = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The per-gender derived <see cref="ArmorSetStatus"/> for the bound set (drives the header badge).</summary>
    public ArmorSetStatus SetStatus
        => _overhaul is not null && _set is not null
            ? _mapping.GetArmorSetStatus(_set, _overhaul.Mappings)
            : ArmorSetStatus.NotStarted;

    public IRelayCommand<PieceEditRowViewModel> AssignDonorCommand =>
        _assignDonorCommand ??= new RelayCommand<PieceEditRowViewModel>(
            row => { if (row is not null) AssignDonor(row); }, row => row is not null && row.SelectedDonor is not null);

    public IRelayCommand<PieceEditRowViewModel> AttachBodyPatchCommand =>
        _attachBodyPatchCommand ??= new RelayCommand<PieceEditRowViewModel>(
            row => { if (row is not null) AttachBodyPatch(row); },
            row => row is not null && row.Mapping is not null && row.SelectedBodyPatch is not null);

    public IRelayCommand<PieceEditRowViewModel> AttachPhysicsPatchCommand =>
        _attachPhysicsPatchCommand ??= new RelayCommand<PieceEditRowViewModel>(
            row => { if (row is not null) AttachPhysicsPatch(row); },
            row => row is not null && row.Mapping is not null && row.SelectedPhysicsPatch is not null);

    public IRelayCommand<PieceEditRowViewModel> UnassignCommand =>
        _unassignCommand ??= new RelayCommand<PieceEditRowViewModel>(
            row => { if (row is not null) Unassign(row); }, row => row is not null && row.Mapping is not null);

    public IRelayCommand<PieceEditRowViewModel> DetachBodyPatchCommand =>
        _detachBodyPatchCommand ??= new RelayCommand<PieceEditRowViewModel>(
            row => { if (row is not null) DetachBodyPatch(row); }, row => row is not null && row.Mapping is not null);

    public IRelayCommand<PieceEditRowViewModel> DetachPhysicsPatchCommand =>
        _detachPhysicsPatchCommand ??= new RelayCommand<PieceEditRowViewModel>(
            row => { if (row is not null) DetachPhysicsPatch(row); }, row => row is not null && row.Mapping is not null);

    public IRelayCommand<PieceEditRowViewModel> SetNotesCommand =>
        _setNotesCommand ??= new RelayCommand<PieceEditRowViewModel>(
            row => { if (row is not null) SetNotes(row, row.Notes); });

    public IRelayCommand ImportPatchCommand =>
        _importPatchCommand ??= new RelayCommand(ImportPatch);

    /// <summary>Binds the editor to one (set, gender, weight) variant of an overhaul project.</summary>
    public void Open(ArmorSet set, Variant variant, Overhaul overhaul, DonorLibraryModel library)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(overhaul);
        ArgumentNullException.ThrowIfNull(library);

        _set = set;
        _variant = variant;
        _overhaul = overhaul;
        _library = library;

        _logger.LogInformation(
            "Opened cell editor for '{Set}' {Gender} {Weight}.", set.DisplayName, variant.Gender, variant.Weight);
        Refresh();
    }

    /// <summary>Clears the editor (popover closed). The host flushes autosave before calling.</summary>
    public void Close()
    {
        _set = null;
        _variant = null;
        _overhaul = null;
        _library = null;
        Rows = Array.Empty<PieceEditRowViewModel>();
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SetStatus));
    }

    /// <summary>Re-projects the piece rows + set status from the authoritative Overhaul mappings.</summary>
    public void Refresh()
    {
        Rows = _variant is null || _overhaul is null || _set is null
            ? Array.Empty<PieceEditRowViewModel>()
            : _variant.Pieces.Select(p => BuildRow(p)).ToList();

        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SetStatus));
    }

    /// <summary>Assigns the row's selected donor as the piece's main donor (Phase 3 command kept).</summary>
    public void AssignDonor(PieceEditRowViewModel row)
    {
        if (!IsOpen) return;
        if (row.SelectedDonor is null) return;

        var donorPiece = DonorCompatibility.FindDonorPiece(
                              row.SelectedDonor.Asset, _variant!.Gender, _variant.Weight, row.Piece.Slot)
                          ?? throw new InvalidOperationException(
                              "Donor provides no variant covering this target shape.");

        _mapping.AssignDonor(_overhaul!, _overhaul!.Catalog!, row.SelectedDonor.Asset, row.Piece, donorPiece);
        AfterEdit();
    }

    public void AttachBodyPatch(PieceEditRowViewModel row)
    {
        if (!IsOpen || row.Mapping is null || row.SelectedBodyPatch is null) return;
        _mapping.AttachPatch(_overhaul!, row.Mapping, row.SelectedBodyPatch.Asset, PatchKind.Body);
        AfterEdit();
    }

    public void AttachPhysicsPatch(PieceEditRowViewModel row)
    {
        if (!IsOpen || row.Mapping is null || row.SelectedPhysicsPatch is null) return;
        _mapping.AttachPatch(_overhaul!, row.Mapping, row.SelectedPhysicsPatch.Asset, PatchKind.Physics);
        AfterEdit();
    }

    public void Unassign(PieceEditRowViewModel row)
    {
        if (!IsOpen || row.Mapping is null) return;
        _mapping.Unassign(_overhaul!, row.Mapping);
        AfterEdit();
    }

    public void DetachBodyPatch(PieceEditRowViewModel row)
    {
        if (!IsOpen || row.Mapping is null) return;
        _mapping.DetachPatch(_overhaul!, row.Mapping, PatchKind.Body);
        AfterEdit();
    }

    public void DetachPhysicsPatch(PieceEditRowViewModel row)
    {
        if (!IsOpen || row.Mapping is null) return;
        _mapping.DetachPatch(_overhaul!, row.Mapping, PatchKind.Physics);
        AfterEdit();
    }

    /// <summary>Edits the notes of the row's mapping, preserving donor/patches/status.</summary>
    public void SetNotes(PieceEditRowViewModel row, string? notes)
    {
        if (!IsOpen || row.Mapping is null) return;

        var m = row.Mapping;
        var updated = new PieceMapping(
            m.Id, m.OverhaulId, m.TargetArmorSetId, m.TargetPieceEditorId, m.TargetGender,
            m.DonorAssetId, m.DonorPieceEditorId, m.DonorMeshPath,
            m.BodyConversionPatchAssetId, m.PhysicsPatchAssetId, m.Status, string.IsNullOrWhiteSpace(notes) ? null : notes);

        var index = _overhaul!.Mappings.FindIndex(x => x.Id == m.Id);
        if (index < 0) return;
        _overhaul.Mappings[index] = updated;
        AfterEdit();
    }

    /// <summary>Opens the donor library so the user can import the missing patch (navigation shortcut).</summary>
    public void ImportPatch()
    {
        _navigation?.Navigate(typeof(DonorLibraryView));
    }

    private void AfterEdit()
    {
        Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private PieceEditRowViewModel BuildRow(Piece piece)
    {
        var gender = _variant!.Gender;
        var weight = _variant!.Weight;
        var mapping = FindMapping(_set!.Id, piece.EditorId, gender);

        var donorAsset = mapping is null
            ? null
            : _library!.Assets.FirstOrDefault(a => a.ImportId == mapping.DonorAssetId);
        var bodyPatch = mapping?.BodyConversionPatchAssetId is { } b
            ? _library!.Assets.FirstOrDefault(a => a.ImportId == b)
            : null;
        var physicsPatch = mapping?.PhysicsPatchAssetId is { } p
            ? _library!.Assets.FirstOrDefault(a => a.ImportId == p)
            : null;

        var status = resolveStatus(mapping, donorAsset, bodyPatch, physicsPatch);

        var donors = _library!.Assets
            .Where(a => a.Kind == DonorAssetKind.FullReplacer && DonorCompatibility.IsCompatible(a, gender, weight))
            .OrderBy(DonorCompatibility.DisplayName)
            .Select(a => new DonorOption(a, DonorCompatibility.DisplayName(a)))
            .ToList();

        var bodyPatches = _library.Assets
            .Where(a => a.Kind == DonorAssetKind.BodyConversionPatch)
            .OrderBy(DonorCompatibility.DisplayName)
            .Select(a => new DonorOption(a, DonorCompatibility.DisplayName(a)))
            .ToList();

        var physicsPatches = _library.Assets
            .Where(a => a.Kind == DonorAssetKind.PhysicsPatch)
            .OrderBy(DonorCompatibility.DisplayName)
            .Select(a => new DonorOption(a, DonorCompatibility.DisplayName(a)))
            .ToList();

        return new PieceEditRowViewModel(
            piece,
            gender,
            weight,
            mapping,
            status,
            donors,
            bodyPatches,
            physicsPatches,
            mapping?.Notes);
    }

    private MappingStatus resolveStatus(PieceMapping? mapping, DonorAsset? donorAsset, DonorAsset? bodyPatch, DonorAsset? physicsPatch)
    {
        if (mapping is null) return MappingStatus.Pending;
        if (donorAsset is null) return mapping.Status;
        return _mapping.GetStatus(mapping, donorAsset, bodyPatch, physicsPatch, _overhaul!.Policy);
    }

    private PieceMapping? FindMapping(string setId, string pieceEditorId, Gender gender)
        => _overhaul?.Mappings.FirstOrDefault(m =>
            m.TargetArmorSetId == setId && m.TargetPieceEditorId == pieceEditorId && m.TargetGender == gender);
}
