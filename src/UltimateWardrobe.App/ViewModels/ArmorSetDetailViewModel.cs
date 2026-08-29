using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.Views;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.App.ViewModels;

using DonorLibraryModel = UltimateWardrobe.Core.Domain.DonorLibrary;

/// <summary>
/// SET-level replacement editor (Phase 6 Sprint 6.9, amendment T2): the rescoped
/// <see cref="ArmorSetDetailViewModel"/>. A transient editor instance is bound to exactly one
/// (set, gender, weight) <see cref="Variant"/> of the owning <see cref="Overhaul"/> and its
/// <see cref="PieceMapping"/>s; it is created per activated cell and disposed (cleared) when the
/// anchored popover closes. The editor publishes <see cref="Changed"/> after every edit so the host
/// re-projects the matrix and flushes the autosave - the grid never holds divergent state.
///
/// The layout mirrors the wireframe: LEFT "ARMOR 1" - the variant's pieces as a READ-ONLY target
/// inventory (<see cref="Rows"/>); RIGHT "ARMOR 2" - ONE donor that replaces the whole set variant
/// (<see cref="LoadDonor"/>, displayed as "Load Armor" until a donor is loaded and "Change" after)
/// with set-level body/physics check rows: the body check is chosen by the replacement gender
/// (female -> 3BA, male -> HIMBO) and is a checkmark when the donor already contains the body
/// (BodySlide flags / <see cref="DonorCompatibility"/> body-marker detection), otherwise a
/// "Load .. patch" row picking ONE specific BodyConversion patch; the physics check mirrors that
/// with the donor <c>DetectedPhysicsFiles</c> flag and ONE specific Physics patch. The Phase 3
/// commands <see cref="MappingService.AssignDonor"/> / <see cref="MappingService.AttachPatch"/> are
/// kept verbatim. Changing the donor fans the new donor out to every piece of the variant and
/// unloads the replaced donor from the library once nothing references it anymore (user-confirmed
/// accounting: a donor that other sets/variants still reference stays). Headless - only
/// <see cref="MappingService"/> + UI abstractions are injected.
/// </summary>
public sealed class ArmorSetDetailViewModel : ObservableObject
{
    private readonly MappingService _mapping;
    private readonly IAppNavigationService? _navigation;
    private readonly IAppDialogService? _dialogs;
    private readonly IDonorImportRunner? _importRunner;
    private readonly ILogger<ArmorSetDetailViewModel> _logger;

    private Overhaul? _overhaul;
    private ArmorSet? _set;
    private Variant? _variant;
    private DonorLibraryModel? _library;
    private Project? _project;
    private IReadOnlyList<PieceInventoryRowViewModel> _rows = Array.Empty<PieceInventoryRowViewModel>();

    private IAsyncRelayCommand? _loadDonorCommand;
    private IRelayCommand? _loadBodyPatchCommand;
    private IRelayCommand? _loadPhysicsPatchCommand;
    private IRelayCommand? _clearBodyPatchCommand;
    private IRelayCommand? _clearPhysicsPatchCommand;
    private IRelayCommand? _importPatchCommand;

    public ArmorSetDetailViewModel(
        MappingService mapping,
        IAppNavigationService? navigation = null,
        IAppDialogService? dialogs = null,
        IDonorImportRunner? importRunner = null,
        ILogger<ArmorSetDetailViewModel>? logger = null)
    {
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _navigation = navigation;
        _dialogs = dialogs;
        _importRunner = importRunner;
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
            if (_set is null || _variant is null) return "Replacement editor";
            return $"{_set.DisplayName} - {_variant.Gender} {_variant.Weight}";
        }
    }

    public IReadOnlyList<PieceInventoryRowViewModel> Rows
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

    // ARMOR 2 - the single replacement donor.

    /// <summary>True while every piece of the variant resolves to exactly one donor asset.</summary>
    public bool HasCurrentDonor => CurrentDonor is not null;

    /// <summary>The loaded donor's display name ("Armor: &lt;mod name&gt;"), empty when nothing is loaded.</summary>
    public string CurrentDonorName => CurrentDonor is { } donor ? DonorCompatibility.DisplayName(donor) : string.Empty;

    /// <summary>"Armor: &lt;mod name&gt;" when loaded, else the empty-state hint.</summary>
    public string CurrentDonorText => HasCurrentDonor ? $"Armor: {CurrentDonorName}" : "Nothing loaded yet";

    /// <summary>The action label of the picker button: "Load Armor" before, "Change" after a donor is loaded.</summary>
    public string LoadDonorLabel => HasCurrentDonor ? "Change" : "Load Armor";

    /// <summary>The body type demanded by the replacement gender: male -> HIMBO, else 3BA.</summary>
    public string RequiredBodyName => _variant is not null && _variant.Gender == Gender.Male ? "HIMBO" : "3BA";

    /// <summary>True when the loaded donor already contains the required body type (3BA/HIMBO).</summary>
    public bool HasRequiredBody => CurrentDonor is { } donor
        && DonorCompatibility.DonorContainsBody(donor, DonorCompatibility.RequiredBodyTypeFor(_variant!.Gender));

    /// <summary>True when the loaded donor already contains physics (HDT-SMP).</summary>
    public bool HasPhysics => CurrentDonor is { } donor && DonorCompatibility.DonorHasPhysics(donor);

    public string BodyCheckText => HasCurrentDonor
        ? (HasRequiredBody ? $"{RequiredBodyName}: OK" : $"{RequiredBodyName}: patch required")
        : string.Empty;

    public string PhysicsCheckText => HasCurrentDonor
        ? (HasPhysics ? "HDT-SMP: OK" : "HDT-SMP: patch required")
        : string.Empty;

    /// <summary>True when the donor lacks the required body AND no body patch is attached yet - shows the "Load .. patch" row.</summary>
    public bool ShowBodyPatchRow => HasCurrentDonor && !HasRequiredBody && !HasAttachedBodyPatch;

    /// <summary>True when the donor lacks physics AND no physics patch is attached yet - shows the "Load HDT-SMP patch" row.</summary>
    public bool ShowPhysicsPatchRow => HasCurrentDonor && !HasPhysics && !HasAttachedPhysicsPatch;

    public bool ShowClearBodyPatch => HasCurrentDonor && !HasRequiredBody && HasAttachedBodyPatch;

    public bool ShowClearPhysicsPatch => HasCurrentDonor && !HasPhysics && HasAttachedPhysicsPatch;

    public bool HasAttachedBodyPatch => _variant is not null && VariantMappings().Any(m => m.BodyConversionPatchAssetId.HasValue);

    public bool HasAttachedPhysicsPatch => _variant is not null && VariantMappings().Any(m => m.PhysicsPatchAssetId.HasValue);

    /// <summary>Label of the body-patch row: "Load 3BA patch" / "Load HIMBO patch".</summary>
    public string LoadBodyPatchLabel => $"Load {RequiredBodyName} patch";

    /// <summary>Label of the clear-body-patch action: "Clear 3BA patch" / "Clear HIMBO patch".</summary>
    public string ClearBodyPatchLabel => $"Clear {RequiredBodyName} patch";

    /// <summary>The one donor replacing this variant (null when loaded donors are mixed/unassigned).</summary>
    private DonorAsset? CurrentDonor
    {
        get
        {
            if (_library is null) return null;

            var donorIds = VariantMappings().Select(m => m.DonorAssetId).Distinct().ToList();
            if (donorIds.Count != 1) return null;
            return _library.Assets.FirstOrDefault(a => a.ImportId == donorIds[0]);
        }
    }

    /// <summary>
    /// The compatible donor candidates of the library (the "load armor" picker). Excludes the directly
    /// loaded donor so a Change always picks a different replacement.
    /// </summary>
    public IReadOnlyList<DonorOption> AvailableDonors
    {
        get
        {
            if (_library is null || _variant is null) return Array.Empty<DonorOption>();

            var current = CurrentDonor;
            return _library.Assets
                .Where(a => a.Kind == DonorAssetKind.FullReplacer
                            && DonorCompatibility.IsCompatible(a, _variant.Gender, _variant.Weight)
                            && a.ImportId != current?.ImportId)
                .OrderBy(DonorCompatibility.DisplayName)
                .Select(a => new DonorOption(a, DonorCompatibility.DisplayName(a)))
                .ToList();
        }
    }

    public IReadOnlyList<DonorOption> BodyPatches
    {
        get
        {
            if (_library is null) return Array.Empty<DonorOption>();
            return _library.Assets
                .Where(a => a.Kind == DonorAssetKind.BodyConversionPatch)
                .OrderBy(DonorCompatibility.DisplayName)
                .Select(a => new DonorOption(a, DonorCompatibility.DisplayName(a)))
                .ToList();
        }
    }

    public IReadOnlyList<DonorOption> PhysicsPatches
    {
        get
        {
            if (_library is null) return Array.Empty<DonorOption>();
            return _library.Assets
                .Where(a => a.Kind == DonorAssetKind.PhysicsPatch)
                .OrderBy(DonorCompatibility.DisplayName)
                .Select(a => new DonorOption(a, DonorCompatibility.DisplayName(a)))
                .ToList();
        }
    }

    private DonorOption? _selectedDonor;
    public DonorOption? SelectedDonor
    {
        get => _selectedDonor;
        set => SetProperty(ref _selectedDonor, value);
    }

    private DonorOption? _selectedBodyPatch;
    public DonorOption? SelectedBodyPatch
    {
        get => _selectedBodyPatch;
        set => SetProperty(ref _selectedBodyPatch, value);
    }

    private DonorOption? _selectedPhysicsPatch;
    public DonorOption? SelectedPhysicsPatch
    {
        get => _selectedPhysicsPatch;
        set => SetProperty(ref _selectedPhysicsPatch, value);
    }

    /// <summary>
    /// "Load Armor" picker action (user finding - a mod with an armor donor must be loadable straight
    /// from the editor): assigns the ComboBox selection when one is made ("Change"), otherwise opens
    /// the mod-archive picker, imports + classifies the archive as a <see cref="DonorAssetKind.FullReplacer"/>
    /// donor through <see cref="DonorImportRunner"/>, and immediately assigns it to the whole variant.
    /// </summary>
    public IAsyncRelayCommand LoadDonorCommand =>
        _loadDonorCommand ??= new AsyncRelayCommand(LoadDonorOrImportAsync);

    public IRelayCommand LoadBodyPatchCommand =>
        _loadBodyPatchCommand ??= new RelayCommand(() => LoadBodyPatch(SelectedBodyPatch?.Asset));

    public IRelayCommand LoadPhysicsPatchCommand =>
        _loadPhysicsPatchCommand ??= new RelayCommand(() => LoadPhysicsPatch(SelectedPhysicsPatch?.Asset));

    public IRelayCommand ClearBodyPatchCommand =>
        _clearBodyPatchCommand ??= new RelayCommand(ClearBodyPatch);

    public IRelayCommand ClearPhysicsPatchCommand =>
        _clearPhysicsPatchCommand ??= new RelayCommand(ClearPhysicsPatch);

    public IRelayCommand ImportPatchCommand =>
        _importPatchCommand ??= new RelayCommand(ImportPatch);

    /// <summary>Binds the editor to one (set, gender, weight) variant of an overhaul project.</summary>
    public void Open(ArmorSet set, Variant variant, Overhaul overhaul, DonorLibraryModel library, Project? project = null)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(overhaul);
        ArgumentNullException.ThrowIfNull(library);

        _set = set;
        _variant = variant;
        _overhaul = overhaul;
        _library = library;
        _project = project;

        _logger.LogInformation(
            "Opened replacement editor for '{Set}' {Gender} {Weight}.", set.DisplayName, variant.Gender, variant.Weight);
        Refresh();
    }

    /// <summary>Clears the editor (popover closed). The host flushes autosave before calling.</summary>
    public void Close()
    {
        _set = null;
        _variant = null;
        _overhaul = null;
        _library = null;
        _project = null;
        Rows = Array.Empty<PieceInventoryRowViewModel>();
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SetStatus));
    }

    /// <summary>Re-projects the inventory rows + ARMOR 2 state from the authoritative Overhaul mappings.</summary>
    public void Refresh()
    {
        Rows = _variant is null || _overhaul is null || _set is null
            ? Array.Empty<PieceInventoryRowViewModel>()
            : _variant.Pieces.Select(BuildRow).ToList();

        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SetStatus));
        OnPropertyChanged(nameof(HasCurrentDonor));
        OnPropertyChanged(nameof(CurrentDonorName));
        OnPropertyChanged(nameof(CurrentDonorText));
        OnPropertyChanged(nameof(LoadDonorLabel));
        OnPropertyChanged(nameof(RequiredBodyName));
        OnPropertyChanged(nameof(HasRequiredBody));
        OnPropertyChanged(nameof(HasPhysics));
        OnPropertyChanged(nameof(BodyCheckText));
        OnPropertyChanged(nameof(PhysicsCheckText));
        OnPropertyChanged(nameof(ShowBodyPatchRow));
        OnPropertyChanged(nameof(ShowPhysicsPatchRow));
        OnPropertyChanged(nameof(ShowClearBodyPatch));
        OnPropertyChanged(nameof(ShowClearPhysicsPatch));
        OnPropertyChanged(nameof(HasAttachedBodyPatch));
        OnPropertyChanged(nameof(HasAttachedPhysicsPatch));
        OnPropertyChanged(nameof(LoadBodyPatchLabel));
        OnPropertyChanged(nameof(ClearBodyPatchLabel));
        OnPropertyChanged(nameof(AvailableDonors));
        OnPropertyChanged(nameof(BodyPatches));
        OnPropertyChanged(nameof(PhysicsPatches));
    }

    /// <summary>
    /// Loads (or changes) the donor replacing the whole set variant: the donor piece is resolved per
    /// target piece via <see cref="DonorCompatibility.FindDonorPiece"/> and assigned through the
    /// Phase 3 <see cref="MappingService.AssignDonor"/> command (replaces the variant's mappings,
    /// clearing stale patch layers). A replaced donor is unloaded from the library once nothing
    /// references it anymore.
    /// </summary>
    public void LoadDonor(DonorAsset? donor)
    {
        if (!IsOpen || donor is null || _overhaul is null || _overhaul.Catalog is null) return;
        if (HasCurrentDonor && CurrentDonor!.ImportId == donor.ImportId) return;

        var previousDonor = CurrentDonor;
        foreach (var piece in _variant!.Pieces)
        {
            var donorPiece = DonorCompatibility.FindDonorPiece(donor, _variant.Gender, _variant.Weight, piece.Slot)
                ?? throw new InvalidOperationException(
                    $"Donor {donor.OriginalFileName} provides no variant covering {_variant.Gender} {_variant.Weight}.");
            _mapping.AssignDonor(_overhaul, _overhaul.Catalog, donor, piece, donorPiece);
        }

        SelectedDonor = null;
        SelectPatchesForCurrentState();
        AfterEdit();
        UnloadDonorIfUnreferenced(previousDonor);
    }

    /// <summary>
    /// The "Load Armor" command body (user finding): a ComboBox selection wins (Change); otherwise the
    /// user is pointed at a mod archive which is imported + classified and, when it truly is a
    /// <see cref="DonorAssetKind.FullReplacer"/> covering the variant's gender/weight, assigned in one
    /// step. A cancelled picker or an unusable archive leaves the editor untouched (the archive stays
    /// in the library so the user can inspect/reclassify it on the Donor Library screen).
    /// </summary>
    private async Task LoadDonorOrImportAsync()
    {
        if (SelectedDonor is not null)
        {
            LoadDonor(SelectedDonor.Asset);
            return;
        }

        if (!IsOpen || _library is null || _project is null || _variant is null || _overhaul is null
            || _importRunner is null || _dialogs is null)
        {
            return;
        }

        var archive = await _dialogs.PickModArchiveAsync(
            "Load Armor - choose the donor mod archive (.7z, .zip, .rar)");
        if (string.IsNullOrWhiteSpace(archive))
        {
            return;
        }

        IReadOnlyList<DonorAsset> imported;
        try
        {
            imported = await _importRunner.ImportAsync(
                new[] { archive },
                _project.RootPath,
                _library,
                _overhaul.Catalog,
                progress: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Importing the donor '{Archive}' from the replacement editor failed.", archive);
            await _dialogs.AlertAsync("Import failed", ex.Message);
            return;
        }

        var asset = imported.FirstOrDefault();
        if (asset is null)
        {
            return;
        }

        if (asset.Kind != DonorAssetKind.FullReplacer
            || !DonorCompatibility.IsCompatible(asset, _variant.Gender, _variant.Weight))
        {
            await _dialogs.AlertAsync(
                "Donor not usable",
                $"'{DonorCompatibility.DisplayName(asset)}' was classified as {DonorPresentation.KindText(asset.Kind)} and provides no "
                + $"{_variant.Gender} {_variant.Weight} variant. It stays in the donor library - check it on the Donor Library screen.");
            return;
        }

        LoadDonor(asset);
    }

    /// <summary>Attaches one specific BodyConversion patch to every mapping of the variant (Phase 3 command kept).</summary>
    public void LoadBodyPatch(DonorAsset? patch)
    {
        if (!IsOpen || patch is null || _overhaul is null || !ShowBodyPatchRow) return;

        foreach (var mapping in VariantMappings())
        {
            _mapping.AttachPatch(_overhaul, mapping, patch, PatchKind.Body);
        }

        AfterEdit();
    }

    /// <summary>Attaches one specific Physics patch to every mapping of the variant (Phase 3 command kept).</summary>
    public void LoadPhysicsPatch(DonorAsset? patch)
    {
        if (!IsOpen || patch is null || _overhaul is null || !ShowPhysicsPatchRow) return;

        foreach (var mapping in VariantMappings())
        {
            _mapping.AttachPatch(_overhaul, mapping, patch, PatchKind.Physics);
        }

        AfterEdit();
    }

    public void ClearBodyPatch()
    {
        if (!IsOpen || _overhaul is null || !ShowClearBodyPatch) return;

        foreach (var mapping in VariantMappings())
        {
            _mapping.DetachPatch(_overhaul, mapping, PatchKind.Body);
        }

        AfterEdit();
    }

    public void ClearPhysicsPatch()
    {
        if (!IsOpen || _overhaul is null || !ShowClearPhysicsPatch) return;

        foreach (var mapping in VariantMappings())
        {
            _mapping.DetachPatch(_overhaul, mapping, PatchKind.Physics);
        }

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

    private IReadOnlyList<PieceMapping> VariantMappings()
    {
        if (_set is null || _variant is null || _overhaul is null) return Array.Empty<PieceMapping>();

        var result = new List<PieceMapping>();
        foreach (var piece in _variant.Pieces)
        {
            var mapping = FindMapping(_set.Id, piece.EditorId, _variant.Gender);
            if (mapping is not null)
            {
                result.Add(mapping);
            }
        }

        return result;
    }

    /// <summary>
    /// Donor-library accounting (user-confirmed, Sprint 6.9 T2): a replaced donor is unloaded only
    /// when no mapping anywhere in the project still references it (as the main donor or an attached
    /// body/physics patch layer); donors other sets/variants still reference stay.
    /// </summary>
    private void UnloadDonorIfUnreferenced(DonorAsset? donor)
    {
        if (donor is null || _library is null) return;

        var stillReferenced = _project?.Overhauls
            .SelectMany(o => o.Mappings)
            .Any(m => m.DonorAssetId == donor.ImportId
                      || m.BodyConversionPatchAssetId == donor.ImportId
                      || m.PhysicsPatchAssetId == donor.ImportId);
        if (stillReferenced is true) return;

        _library.Assets.Remove(donor);
        _logger.LogInformation(
            "Unloaded donor '{Donor}' from the library - nothing references it anymore.", DonorCompatibility.DisplayName(donor));
    }

    /// <summary>
    /// Seeds the patch dropdowns from the effectively attached layers. A donor change clears them
    /// (LoadDonor replaces the mappings), but a fresh open over pre-existing per-piece mappings keeps
    /// the selection aligned with what is attached.
    /// </summary>
    private void SelectPatchesForCurrentState()
    {
        var mappings = VariantMappings();
        SelectedBodyPatch = BodyPatches.FirstOrDefault(o =>
            mappings.Any(m => m.BodyConversionPatchAssetId == o.Asset.ImportId));
        SelectedPhysicsPatch = PhysicsPatches.FirstOrDefault(o =>
            mappings.Any(m => m.PhysicsPatchAssetId == o.Asset.ImportId));
    }

    private PieceInventoryRowViewModel BuildRow(Piece piece)
    {
        var gender = _variant!.Gender;
        var weight = _variant.Weight;
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
        var donorBodyMarker = mapping is null ? null : MappingService.BodyMarkerFromPath(mapping.DonorMeshPath);

        return new PieceInventoryRowViewModel(
            piece,
            gender,
            weight,
            mapping,
            status,
            donorBodyMarker,
            donorAsset?.DetectedBodySlideFiles.Count > 0,
            donorAsset?.DetectedPhysicsFiles.Count > 0);
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