using CommunityToolkit.Mvvm.ComponentModel;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>One selectable donor/patch library asset in an editor dropdown (Phase 6 Sprint 6.5).</summary>
public sealed record DonorOption(DonorAsset Asset, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// One target piece row of the single-cell editor (Phase 6 Sprint 6.5). Carries the piece identity,
/// its current <see cref="PieceMapping"/>, the derived <see cref="MappingStatus"/> (so a
/// <see cref="MappingStatus.NeedsPatch"/> row highlights and offers the patch panel), the compatible
/// "load armor" donor candidates and the BodyConversion/Physics patch candidates, plus the donor / patch
/// badges. Rows are lightweight projections rebuilt by <see cref="ArmorSetDetailViewModel.Refresh"/>
/// after each edit, so they never hold divergent state.
/// </summary>
public sealed class PieceEditRowViewModel : ObservableObject
{
    private DonorOption? _selectedDonor;
    private DonorOption? _selectedBodyPatch;
    private DonorOption? _selectedPhysicsPatch;
    private string? _notes;

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
    public bool ShowPatchPanel => IsNeedsPatch;

    /// <summary>Badges from the mapped donor (Phase 6 Sprint 6.5): body marker + detected BodySlide/physics.</summary>
    public BodyType? DonorBodyMarker { get; }
    public string? DonorBodyMarkerText => DonorBodyMarker is { } b && b != BodyType.Unknown ? b.ToString() : null;
    public bool HasBodySlide { get; }
    public bool HasPhysics { get; }

    public IReadOnlyList<DonorOption> Donors { get; }
    public IReadOnlyList<DonorOption> BodyPatches { get; }
    public IReadOnlyList<DonorOption> PhysicsPatches { get; }

    public DonorOption? SelectedDonor
    {
        get => _selectedDonor;
        set => SetProperty(ref _selectedDonor, value);
    }

    public DonorOption? SelectedBodyPatch
    {
        get => _selectedBodyPatch;
        set => SetProperty(ref _selectedBodyPatch, value);
    }

    public DonorOption? SelectedPhysicsPatch
    {
        get => _selectedPhysicsPatch;
        set => SetProperty(ref _selectedPhysicsPatch, value);
    }

    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public PieceEditRowViewModel(
        Piece piece,
        Gender gender,
        WeightClass weight,
        PieceMapping? mapping,
        MappingStatus status,
        IReadOnlyList<DonorOption> donors,
        IReadOnlyList<DonorOption> bodyPatches,
        IReadOnlyList<DonorOption> physicsPatches,
        string? notes)
    {
        Piece = piece;
        Gender = gender;
        Weight = weight;
        Mapping = mapping;
        Status = status;
        Donors = donors;
        BodyPatches = bodyPatches;
        PhysicsPatches = physicsPatches;
        Notes = notes;

        _selectedDonor = donors.FirstOrDefault(d => d.Asset.ImportId == mapping?.DonorAssetId);
        _selectedBodyPatch = bodyPatches.FirstOrDefault(p => p.Asset.ImportId == mapping?.BodyConversionPatchAssetId);
        _selectedPhysicsPatch = physicsPatches.FirstOrDefault(p => p.Asset.ImportId == mapping?.PhysicsPatchAssetId);
    }
}
