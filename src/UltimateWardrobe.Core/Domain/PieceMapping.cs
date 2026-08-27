using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Core.Domain;

public sealed class PieceMapping
{
    public Guid Id { get; init; }
    public Guid OverhaulId { get; init; }
    public string TargetArmorSetId { get; init; }
    public string TargetPieceEditorId { get; init; }
    public Gender TargetGender { get; init; }
    public Guid DonorAssetId { get; init; }
    public string DonorPieceEditorId { get; init; }
    public string DonorMeshPath { get; init; }
    public Guid? BodyConversionPatchAssetId { get; init; }
    public Guid? PhysicsPatchAssetId { get; init; }
    public MappingStatus Status { get; init; }
    public string? Notes { get; init; }

    public PieceMapping(
        Guid id,
        Guid overhaulId,
        string targetArmorSetId,
        string targetPieceEditorId,
        Gender targetGender,
        Guid donorAssetId,
        string donorPieceEditorId,
        string donorMeshPath,
        Guid? bodyConversionPatchAssetId = null,
        Guid? physicsPatchAssetId = null,
        MappingStatus status = MappingStatus.Mapped,
        string? notes = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id must not be empty.", nameof(id));
        if (overhaulId == Guid.Empty) throw new ArgumentException("OverhaulId must not be empty.", nameof(overhaulId));
        if (string.IsNullOrWhiteSpace(targetArmorSetId)) throw new ArgumentException("TargetArmorSetId must not be empty.", nameof(targetArmorSetId));
        if (string.IsNullOrWhiteSpace(targetPieceEditorId)) throw new ArgumentException("TargetPieceEditorId must not be empty.", nameof(targetPieceEditorId));
        if (donorAssetId == Guid.Empty) throw new ArgumentException("DonorAssetId must not be empty.", nameof(donorAssetId));
        if (string.IsNullOrWhiteSpace(donorPieceEditorId)) throw new ArgumentException("DonorPieceEditorId must not be empty.", nameof(donorPieceEditorId));
        if (string.IsNullOrWhiteSpace(donorMeshPath)) throw new ArgumentException("DonorMeshPath must not be empty.", nameof(donorMeshPath));

        Id = id;
        OverhaulId = overhaulId;
        TargetArmorSetId = targetArmorSetId;
        TargetPieceEditorId = targetPieceEditorId;
        TargetGender = targetGender;
        DonorAssetId = donorAssetId;
        DonorPieceEditorId = donorPieceEditorId;
        DonorMeshPath = donorMeshPath;
        BodyConversionPatchAssetId = bodyConversionPatchAssetId;
        PhysicsPatchAssetId = physicsPatchAssetId;
        Status = status;
        Notes = notes;
    }

    public string UniqueKey => $"{OverhaulId}:{TargetPieceEditorId}:{TargetGender}";

    public void ValidateCrossProject(IReadOnlyCollection<DonorAsset> allowedAssets)
    {
        if (allowedAssets is null) throw new ArgumentNullException(nameof(allowedAssets));
        if (!allowedAssets.Any(a => a.ImportId == DonorAssetId))
        {
            throw new InvalidOperationException($"DonorAsset {DonorAssetId} does not belong to the same project.");
        }

        if (BodyConversionPatchAssetId.HasValue && !allowedAssets.Any(a => a.ImportId == BodyConversionPatchAssetId.Value))
        {
            throw new InvalidOperationException($"BodyConversionPatch {BodyConversionPatchAssetId} does not belong to the same project.");
        }

        if (PhysicsPatchAssetId.HasValue && !allowedAssets.Any(a => a.ImportId == PhysicsPatchAssetId.Value))
        {
            throw new InvalidOperationException($"PhysicsPatch {PhysicsPatchAssetId} does not belong to the same project.");
        }
    }
}
