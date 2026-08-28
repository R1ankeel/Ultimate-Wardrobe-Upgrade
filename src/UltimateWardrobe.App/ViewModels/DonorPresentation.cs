using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Read-only display helpers for the donor library table (Phase 6 Sprint 6.3): a Kind badge string
/// (roadmap 4.3) and the <see cref="DonorAsset"/> -> <see cref="DonorRowViewModel"/> projection. Kept
/// separate from the view model so the badge logic is a pure, unit-testable function.
/// </summary>
public static class DonorPresentation
{
    /// <summary>Badge label for a donor <see cref="DonorAssetKind"/> (roadmap 4.3 table).</summary>
    public static string KindText(DonorAssetKind kind) => kind switch
    {
        DonorAssetKind.FullReplacer => "Full replacer",
        DonorAssetKind.BodyConversionPatch => "Body conversion patch",
        DonorAssetKind.PhysicsPatch => "Physics patch",
        _ => "Unknown",
    };

    /// <summary>Maps the badge label back to its <see cref="DonorAssetKind"/>, or null for unrecognized text.</summary>
    public static DonorAssetKind? ParseKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var kind in new[] { DonorAssetKind.FullReplacer, DonorAssetKind.BodyConversionPatch, DonorAssetKind.PhysicsPatch })
        {
            if (string.Equals(KindText(kind), text, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        if (Enum.TryParse<DonorAssetKind>(text, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>Builds a row for the donor table from a library asset.</summary>
    public static DonorRowViewModel ToRow(DonorAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new DonorRowViewModel(
            asset,
            asset.OriginalFileName,
            KindText(asset.Kind),
            asset.ProvidedSets.Count,
            asset.DetectedBodySlideFiles.Count > 0,
            asset.DetectedPhysicsFiles.Count > 0,
            asset.ImportedAt.ToString("yyyy-MM-dd HH:mm"));
    }
}

/// <summary>
/// One row in the donor library table (Phase 6 Sprint 6.3): the living <see cref="DonorAsset"/>
/// reference (so the remove/reclassify/kind commands can act on it) plus the displayed fields -
/// Kind badge, ProvidedSets count, BodySlide/physics indicators and import date.
/// </summary>
public sealed record DonorRowViewModel(
    DonorAsset Asset,
    string OriginalFileName,
    string KindText,
    int ProvidedSetsCount,
    bool HasBodySlide,
    bool HasPhysics,
    string ImportDate)
{
    public Guid ImportId => Asset.ImportId;
}
