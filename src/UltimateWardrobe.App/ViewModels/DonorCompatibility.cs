using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Donor-vs-target compatibility filtering + donor-piece resolution (Phase 6 Sprint 6.5, refactored F1).
/// A donor is a candidate "load armor" for a target <see cref="Piece"/> only when it provides a variant
/// that can back the target's gender - its provided set contains a variant of the same gender (or
/// <see cref="Gender.Unisex"/>). Weight is intentionally ignored: any vanilla item (Heavy / Light /
/// Clothing) may be replaced by any donor item sharing the same biped slot (F1 spec). The piece
/// actually referenced in an <see cref="MappingService.AssignDonor"/> call is resolved from that same
/// annotated variant, preferring a donor piece whose <see cref="Piece.Slot"/> matches the target slot.
/// </summary>
public static class DonorCompatibility
{
    /// <summary>
    /// True when <paramref name="donor"/> offers a variant able to back <paramref name="gender"/>.
    /// Weight is ignored (F1). A donor with no classified provided sets is not compatible (it cannot
    /// prove it covers the target shape) and is filtered out of the "load armor" dropdown.
    /// </summary>
    public static bool IsCompatible(DonorAsset donor, Gender gender)
    {
        if (donor is null) return false;

        foreach (var set in donor.ProvidedSets)
        {
            foreach (var variant in set.Variants)
            {
                if (VariantMatches(variant, gender))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Backward-compatible overload - weight is ignored (F1). Prefer <see cref="IsCompatible(DonorAsset,Gender)"/>.
    /// </summary>
    public static bool IsCompatible(DonorAsset donor, Gender gender, WeightClass weight)
        => IsCompatible(donor, gender);

    /// <summary>
    /// Resolves the donor <see cref="Piece"/> used to back <paramref name="targetSlot"/>: the first
    /// matching gender variant of the donor whose slot is compatible with <paramref name="targetSlot"/>
    /// via <see cref="SlotNormalizer"/> (F2). Weight is ignored (F1). No fallback to the first piece -
    /// a missing slot returns null so the caller can surface a per-piece warning while allowing partial
    /// assignment for the remaining slots.
    /// </summary>
    public static Piece? FindDonorPiece(DonorAsset donor, Gender gender, string targetSlot)
    {
        if (donor is null) return null;

        foreach (var set in donor.ProvidedSets)
        {
            foreach (var variant in set.Variants)
            {
                if (!VariantMatches(variant, gender))
                {
                    continue;
                }

                var bySlot = variant.Pieces.FirstOrDefault(p => SlotNormalizer.AreCompatible(p.Slot, targetSlot));
                if (bySlot is not null) return bySlot;
            }
        }

        return null;
    }

    /// <summary>
    /// Backward-compatible overload - weight is ignored (F1). Prefer <see cref="FindDonorPiece(DonorAsset,Gender,string)"/>.
    /// </summary>
    public static Piece? FindDonorPiece(DonorAsset donor, Gender gender, WeightClass weight, string targetSlot)
        => FindDonorPiece(donor, gender, targetSlot);

    /// <summary>Display name for a donor option: first provided set's display name, else the archive name.</summary>
    public static string DisplayName(DonorAsset donor)
        => donor.ProvidedSets.Count > 0 ? donor.ProvidedSets[0].DisplayName : donor.OriginalFileName;

    /// <summary>
    /// The body type demanded by a replacement of <paramref name="gender"/> (Phase 6 Sprint 6.9
    /// replacement editor): a female replacement demands 3BA, a male replacement demands HIMBO.
    /// </summary>
    public static BodyType RequiredBodyTypeFor(Gender gender) => gender == Gender.Male ? BodyType.HIMBO : BodyType.ThreeBA;

    /// <summary>
    /// Set-level body requirement check (Phase 6 Sprint 6.9, tightened F3): true only when the donor
    /// carries the required body type in a provided mesh path (via <see cref="MappingService.BodyMarkerFromPath"/>)
    /// or in a detected BodySlide file path. A generic <c>DetectedBodySlideFiles.Count &gt; 0</c> alone
    /// no longer satisfies the check - a male HIMBO mesh token does not count for a female 3BA
    /// requirement and vice versa. This enforces the per-gender patch demand from the spec
    /// (Female -&gt; 3BA, Male -&gt; HIMBO).
    /// </summary>
    public static bool DonorContainsBody(DonorAsset donor, BodyType requiredBody)
    {
        if (donor is null) return false;

        foreach (var set in donor.ProvidedSets)
        {
            foreach (var variant in set.Variants)
            {
                foreach (var piece in variant.Pieces)
                {
                    if (MappingService.BodyMarkerFromPath(piece.MeshPath) == requiredBody)
                    {
                        return true;
                    }
                }
            }
        }

        foreach (var bodySlideFile in donor.DetectedBodySlideFiles)
        {
            if (MappingService.BodyMarkerFromPath(bodySlideFile) == requiredBody)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Set-level physics requirement check (Phase 6 Sprint 6.9): true when the donor already contains
    /// physics via the existing <c>DetectedPhysicsFiles</c> flag.
    /// </summary>
    public static bool DonorHasPhysics(DonorAsset donor) => donor is not null && donor.DetectedPhysicsFiles.Count > 0;

    private static bool VariantMatches(Variant variant, Gender gender)
        => variant.Gender == gender || variant.Gender == Gender.Unisex;
}
