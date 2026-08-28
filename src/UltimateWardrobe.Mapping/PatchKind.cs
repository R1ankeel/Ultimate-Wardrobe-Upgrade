namespace UltimateWardrobe.Mapping;

/// <summary>
/// The patch layer of a PieceMapping: which of the two optional patch slots a patch attach call
/// targets - the body-conversion layer (BodyConversionPatchAssetId) or the physics layer
/// (PhysicsPatchAssetId). Distinct from DonorAssetKind: <c>Body</c> pairs with
/// <c>DonorAssetKind.BodyConversionPatch</c> and <c>Physics</c> with <c>DonorAssetKind.PhysicsPatch</c>.
/// </summary>
public enum PatchKind
{
    Body = 0,
    Physics = 1
}
