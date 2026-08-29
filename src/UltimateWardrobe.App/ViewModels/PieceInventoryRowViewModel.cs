using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>One selectable donor/patch library asset in an editor dropdown (Phase 6 Sprint 6.5; reused by the set-level replacement editor in 6.9; extended 3.5 with body/physics preview per donor).</summary>
public sealed class DonorOption
{
    public DonorAsset Asset { get; }
    public string DisplayName { get; }

    public DonorOption(DonorAsset asset, string displayName)
    {
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    public string? BodyMarkerText
    {
        get
        {
            var has3ba = DonorCompatibility.DonorContainsBody(Asset, BodyType.ThreeBA);
            var hasHimbo = DonorCompatibility.DonorContainsBody(Asset, BodyType.HIMBO);
            if (has3ba && hasHimbo) return "3BA+HIMBO";
            if (has3ba) return "3BA";
            if (hasHimbo) return "HIMBO";
            // Fallback to first raw marker (CBBE etc) for display
            var first = Asset.ProvidedSets.SelectMany(s => s.Variants).SelectMany(v => v.Pieces)
                .Select(p => MappingService.BodyMarkerFromPath(p.MeshPath)).FirstOrDefault(m => m is not null);
            return first?.ToString();
        }
    }

    public bool HasThreeBA => DonorCompatibility.DonorContainsBody(Asset, BodyType.ThreeBA);
    public bool HasHimbo => DonorCompatibility.DonorContainsBody(Asset, BodyType.HIMBO);
    public bool HasPhysics => DonorCompatibility.DonorHasPhysics(Asset);
    public bool HasBodySlide => Asset.DetectedBodySlideFiles.Count > 0;

    public override string ToString() => DisplayName;
    public override bool Equals(object? obj) => obj is DonorOption other && Asset.ImportId == other.Asset.ImportId;
    public override int GetHashCode() => Asset.ImportId.GetHashCode();
}

/// <summary>
/// One target piece row in the set-level replacement editor (Phase 6 Sprint 6.9, extended 3.4):
/// carries the piece identity, its current <see cref="PieceMapping"/>, the derived <see cref="MappingStatus"/>,
/// badges, and per-row donor selection via <see cref="SlotNormalizer"/> (F2) - each row filters
/// <see cref="AvailableDonorsForRow"/> to donors that have at least one piece whose canonical slot
/// matches the row's <see cref="Slot"/>. Rows are projections rebuilt by <see cref="ArmorSetDetailViewModel.Refresh"/>.
/// </summary>
public sealed partial class PieceInventoryRowViewModel : ObservableObject
{
    private readonly Action<PieceInventoryRowViewModel>? _assignCallback;

    public Piece Piece { get; }
    public Gender Gender { get; }
    public WeightClass Weight { get; }
    public PieceMapping? Mapping { get; }
    public MappingStatus Status { get; }

    public string EditorId => Piece.EditorId;
    public string Slot => Piece.Slot;
    public string? TargetMesh => Piece.MeshPath;
    public string? ArmaEditorId => Piece.ArmaEditorId;

    public bool IsAssigned => Mapping is not null;
    public bool IsNeedsPatch => Status == MappingStatus.NeedsPatch;

    /// <summary>Badges from the mapped donor: body marker + detected BodySlide/physics.</summary>
    public BodyType? DonorBodyMarker { get; }
    public string? DonorBodyMarkerText => DonorBodyMarker is { } b && b != BodyType.Unknown ? b.ToString() : null;
    public bool HasBodySlide { get; }
    public bool HasPhysics { get; }

    public IReadOnlyList<DonorOption> AvailableDonorsForRow { get; }

    [ObservableProperty]
    private DonorOption? _selectedDonor;

    public IRelayCommand AssignDonorCommand { get; }

    public PieceInventoryRowViewModel(
        Piece piece,
        Gender gender,
        WeightClass weight,
        PieceMapping? mapping,
        MappingStatus status,
        BodyType? donorBodyMarker,
        bool hasBodySlide,
        bool hasPhysics,
        IReadOnlyList<DonorOption>? availableDonorsForRow = null,
        DonorOption? selectedDonor = null,
        Action<PieceInventoryRowViewModel>? assignCallback = null)
    {
        Piece = piece;
        Gender = gender;
        Weight = weight;
        Mapping = mapping;
        Status = status;
        DonorBodyMarker = donorBodyMarker;
        HasBodySlide = hasBodySlide;
        HasPhysics = hasPhysics;
        AvailableDonorsForRow = availableDonorsForRow ?? Array.Empty<DonorOption>();
        _selectedDonor = selectedDonor;
        _assignCallback = assignCallback;
        AssignDonorCommand = new RelayCommand(ExecuteAssign, CanAssign);
    }

    private bool CanAssign() => SelectedDonor is not null;

    private void ExecuteAssign()
    {
        if (SelectedDonor is null) return;
        _assignCallback?.Invoke(this);
    }

    partial void OnSelectedDonorChanged(DonorOption? value)
    {
        AssignDonorCommand.NotifyCanExecuteChanged();
    }
}