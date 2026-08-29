using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>One selectable donor/patch library asset in an editor dropdown (Phase 6 Sprint 6.5; reused by the set-level replacement editor in 6.9).</summary>
public sealed record DonorOption(DonorAsset Asset, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// One read-only target piece row of the left "ARMOR 1" inventory in the set-level replacement
/// editor (Phase 6 Sprint 6.9): carries the piece identity, its current <see cref="PieceMapping"/>,
/// the derived <see cref="MappingStatus"/> (so a <see cref="MappingStatus.NeedsPatch"/> inventory row
/// highlights), and the badges from its mapped donor (body marker via
/// <see cref="MappingService.BodyMarkerFromPath"/> + detected BodySlide/physics). Rows are lightweight
/// projections rebuilt by <see cref="ArmorSetDetailViewModel.Refresh"/> after each edit, so they never
/// hold divergent state.
/// </summary>
public sealed class PieceInventoryRowViewModel
{
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

    public PieceInventoryRowViewModel(
        Piece piece,
        Gender gender,
        WeightClass weight,
        PieceMapping? mapping,
        MappingStatus status,
        BodyType? donorBodyMarker,
        bool hasBodySlide,
        bool hasPhysics)
    {
        Piece = piece;
        Gender = gender;
        Weight = weight;
        Mapping = mapping;
        Status = status;
        DonorBodyMarker = donorBodyMarker;
        HasBodySlide = hasBodySlide;
        HasPhysics = hasPhysics;
    }
}