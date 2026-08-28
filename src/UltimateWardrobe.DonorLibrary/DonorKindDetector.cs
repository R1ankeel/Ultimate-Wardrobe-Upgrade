using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Branch-3 Kind table (Sprint 2.3.3, plan section 4.3) as pure, unit-testable logic:
///
/// <code>
/// ProvidesSets &gt; 0 AND brought via branch 1 (donor ARMO)
///   OR meshes grouped into &gt;= 1 set with a body piece  -&gt; FullReplacer
/// else SliderSets present                                    -&gt; BodyConversionPatch
/// else physics files present                                 -&gt; PhysicsPatch
/// else                                                        -&gt; Unknown
/// </code>
///
/// Flags are independent of <c>Kind</c>: a <see cref="DonorAssetKind.FullReplacer"/> may carry
/// BodySlide/physics flags - only <c>Kind</c> chooses the primary lane. The "body piece" rule is
/// what separates a genuine mesh-only body/armor replacer from a stray model resource: a branch-2
/// set qualifies only when at least one of its pieces covers the body/torso
/// (<c>Body</c>/<c>Skin</c>/<c>Cuirass</c>/<c>Armor</c>/<c>Clothes</c>/<c>Robe</c>/<c>Robes</c>/<c>Dress</c>).
/// Recalibration point: real-donor tuning in Sprint 2.5.
/// </summary>
public static class DonorKindDetector
{
    private static readonly IReadOnlySet<string> BodyPieceSlots = new HashSet<string>(StringComparer.Ordinal)
    {
        "Body",
        "Skin",
        "Cuirass",
        "Armor",
        "Clothes",
        "Robe",
        "Robes",
        "Dress",
    };

    public static DonorAssetKind Derive(
        IReadOnlyList<DonorProvidedSet> providedSets,
        bool setsBroughtViaBranch1,
        IReadOnlyList<string> bodySlideFiles,
        IReadOnlyList<string> physicsFiles)
    {
        var isFullReplacer = (setsBroughtViaBranch1 && providedSets.Count > 0)
            || (!setsBroughtViaBranch1 && providedSets.Any(HasBodyPiece));

        if (isFullReplacer)
        {
            return DonorAssetKind.FullReplacer;
        }

        if (bodySlideFiles.Count > 0)
        {
            return DonorAssetKind.BodyConversionPatch;
        }

        if (physicsFiles.Count > 0)
        {
            return DonorAssetKind.PhysicsPatch;
        }

        return DonorAssetKind.Unknown;
    }

    private static bool HasBodyPiece(DonorProvidedSet set)
    {
        return set.Variants.Any(variant => variant.Pieces.Any(piece => BodyPieceSlots.Contains(piece.Slot)));
    }
}