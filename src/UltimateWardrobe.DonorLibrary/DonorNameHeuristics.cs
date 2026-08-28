using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Branch-2 name/path heuristics (Sprint 2.2.2) over mesh/texture stems and relative paths.
/// Reuses the frozen Phase 1 detectors where they fit: piece words come from
/// <see cref="PieceTypeDetector.EquipmentWords"/> (matched case-insensitively - real mesh stems
/// are lowercase, unlike CamelCase ARMO EditorIDs), gender from
/// <see cref="GenderWeightDetector.ExplicitFromEditorId"/> then
/// <see cref="GenderWeightDetector.ExplicitFromMeshPath"/>, weight from path tokens.
/// Extensions documented here: single-char <c>f</c>/<c>m</c> path segments count as gender
/// markers (common in chaotic real-world replacer layouts, e.g. <c>meshes/armor/x/f/</c>), and
/// <c>_0</c>/<c>_1</c>/<c>_1st</c> weight markers are stripped for EditorId + primary-file
/// preference (<c>_1</c> &gt; <c>_0</c> &gt; <c>_1st</c> &gt; plain).
/// </summary>
public static class DonorNameHeuristics
{
    /// <summary>
    /// EditorId weight markers: a trailing <c>_0</c>/<c>_1</c>/<c>_1st</c> is a BodySlide
    /// weight-variant suffix, not part of the piece name.
    /// </summary>
    private static readonly IReadOnlyList<string> WeightMarkers =
    [
        "_0",
        "_1",
        "_1st",
    ];

    /// <summary>
    /// Trailing gender markers stripped before piece-word detection (never from the stored
    /// EditorId, which keeps them for Phase 3 display).
    /// </summary>
    private static readonly IReadOnlyList<string> WordMarkers =
    [
        "_female",
        "-female",
        "_male",
        "-male",
        "_1st",
        "_0",
        "_1",
        "_f",
        "-f",
        "_m",
        "-m",
    ];

    /// <summary>
    /// Strips trailing <c>_0</c>/<c>_1</c>/<c>_1st</c> weight markers from a mesh stem, so
    /// <c>cuirass_1</c>, <c>cuirass_0</c> and <c>cuirass</c> collapse to the same EditorId
    /// <c>cuirass</c>. Repeats for stacked markers (<c>cuirass_1_0</c> -&gt; <c>cuirass</c>).
    /// </summary>
    public static string BaseStem(string stem)
    {
        if (stem.Length == 0)
        {
            return stem;
        }

        var result = stem;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var marker in WeightMarkers)
            {
                if (result.EndsWith(marker, StringComparison.OrdinalIgnoreCase) && result.Length >= marker.Length)
                {
                    result = result[..^marker.Length];
                    changed = true;
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Primary-file preference among the members of one EditorId group:
    /// <c>_1</c>(0) &gt; <c>_0</c>(1) &gt; <c>_1st</c>(2) &gt; plain base (3) &gt; anything else (4).
    /// </summary>
    public static int PrimaryRank(string stem, string baseStem)
    {
        if (string.Equals(stem, baseStem, StringComparison.Ordinal))
        {
            return 3;
        }

        if (stem.EndsWith("_1", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (stem.EndsWith("_0", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (stem.EndsWith("_1st", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 4;
    }

    /// <summary>
    /// Piece-type word from a mesh/texture stem via the frozen Phase 1 equipment-word table,
    /// matched case-insensitively after the trailing gender/weight markers are stripped
    /// (<c>cuirass_f</c> -&gt; <c>Cuirass</c>), or null when no known suffix is present.
    /// </summary>
    public static string? PieceTypeFromStem(string stem)
    {
        var cleaned = StripTrailingMarkers(stem);
        if (cleaned.Length == 0)
        {
            return null;
        }

        foreach (var word in PieceTypeDetector.EquipmentWords.OrderByDescending(w => w.Length))
        {
            if (cleaned.EndsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                return word;
            }
        }

        return null;
    }

    /// <summary>
    /// Gender from a mesh stem + relative path. Resolution order: explicit stem markers
    /// (<c>_f</c>/<c>_female</c>/<c>_m</c>/<c>-male</c>, via the Phase 1 EditorID token table), then
    /// <c>female</c>/<c>male</c> path segments (via <see cref="GenderWeightDetector.ExplicitFromMeshPath"/>),
    /// then single-char <c>f</c>/<c>m</c> path segments. Null means "no signal" - the caller falls
    /// back to Unisex.
    /// </summary>
    public static Gender? GenderFrom(string stem, string relativePath)
    {
        var idSignal = GenderWeightDetector.ExplicitFromEditorId(stem);
        if (idSignal is not null)
        {
            return idSignal;
        }

        var pathSignal = GenderWeightDetector.ExplicitFromMeshPath(relativePath);
        if (pathSignal is not null)
        {
            return pathSignal;
        }

        foreach (var segment in relativePath.Replace('\\', '/').Split('/'))
        {
            if (string.Equals(segment, "f", StringComparison.OrdinalIgnoreCase))
            {
                return Gender.Female;
            }

            if (string.Equals(segment, "m", StringComparison.OrdinalIgnoreCase))
            {
                return Gender.Male;
            }
        }

        return null;
    }

    /// <summary>
    /// Weight class from path segments (folders + stem): a segment <c>containing</c> <c>heavy</c>
    /// wins, then <c>light</c>, then <c>clothes</c> - the same priority as the branch-1 keyword
    /// rules. Substring semantics (not whole-word) because real replacer folders mix markers
    /// (<c>heavyiron</c>, <c>lightleather</c>, <c>cuirass_clothes.nif</c>). No matching segment
    /// yields <see cref="WeightClass.Any"/>.
    /// </summary>
    public static WeightClass WeightFromPath(string relativePath)
    {
        var segments = relativePath.ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
        var heavy = segments.Any(s => s.Contains("heavy", StringComparison.Ordinal));
        var light = segments.Any(s => s.Contains("light", StringComparison.Ordinal));
        var clothes = segments.Any(s => s.Contains("clothes", StringComparison.Ordinal));

        if (heavy)
        {
            return WeightClass.Heavy;
        }

        if (light)
        {
            return WeightClass.Light;
        }

        if (clothes)
        {
            return WeightClass.Clothing;
        }

        return WeightClass.Any;
    }

    private static string StripTrailingMarkers(string stem)
    {
        var result = stem;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var marker in WordMarkers)
            {
                if (result.EndsWith(marker, StringComparison.OrdinalIgnoreCase) && result.Length > marker.Length)
                {
                    result = result[..^marker.Length];
                    changed = true;
                    break;
                }
            }
        }

        return result;
    }
}