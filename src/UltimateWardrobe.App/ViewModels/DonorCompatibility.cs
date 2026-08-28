using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Donor-vs-target compatibility filtering + donor-piece resolution (Phase 6 Sprint 6.5). A donor is
/// a candidate "load armor" for a target <see cref="Piece"/> only when it provides a variant that can
/// back the target's gender/weight - its provided set contains a variant of the same gender (or
/// <see cref="Gender.Unisex"/>) and the same weight class (or <see cref="WeightClass.Any"/>). The piece
/// actually referenced in an <see cref="MappingService.AssignDonor"/> call is resolved from that same
/// annotated variant, preferring a donor piece whose <see cref="Piece.Slot"/> matches the target slot.
/// </summary>
public static class DonorCompatibility
{
    /// <summary>
    /// True when <paramref name="donor"/> offers a variant able to back <paramref name="gender"/> +
    /// <paramref name="weight"/>. A donor with no classified provided sets is not compatible (it cannot
    /// prove it covers the target shape) and is filtered out of the "load armor" dropdown.
    /// </summary>
    public static bool IsCompatible(DonorAsset donor, Gender gender, WeightClass weight)
    {
        if (donor is null) return false;

        foreach (var set in donor.ProvidedSets)
        {
            foreach (var variant in set.Variants)
            {
                if (VariantMatches(variant, gender, weight))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the donor <see cref="Piece"/> used to back <paramref name="targetSlot"/>: the first
    /// matching variant of the donor, preferring a piece whose slot equals the target slot, otherwise
    /// the first piece of that variant. Returns null when no compatible variant exists.
    /// </summary>
    public static Piece? FindDonorPiece(DonorAsset donor, Gender gender, WeightClass weight, string targetSlot)
    {
        if (donor is null) return null;

        foreach (var set in donor.ProvidedSets)
        {
            foreach (var variant in set.Variants)
            {
                if (!VariantMatches(variant, gender, weight))
                {
                    continue;
                }

                var bySlot = variant.Pieces.FirstOrDefault(p => p.Slot == targetSlot);
                if (bySlot is not null) return bySlot;
                if (variant.Pieces.Count > 0) return variant.Pieces[0];
            }
        }

        return null;
    }

    /// <summary>Display name for a donor option: first provided set's display name, else the archive name.</summary>
    public static string DisplayName(DonorAsset donor)
        => donor.ProvidedSets.Count > 0 ? donor.ProvidedSets[0].DisplayName : donor.OriginalFileName;

    private static bool VariantMatches(Variant variant, Gender gender, WeightClass weight)
        => (variant.Gender == gender || variant.Gender == Gender.Unisex)
           && (variant.Weight == weight || variant.Weight == WeightClass.Any);
}
