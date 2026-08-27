namespace UltimateWardrobe.Scanner;

/// <summary>
/// Result of normalizing an EditorID (or Outfit EditorID, or mesh folder) into a set key.
/// </summary>
public sealed record NormalizedSetKey
{
    /// <summary>Lowercase, alphanumeric-only set identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Title-case display name derived from the CamelCase source.</summary>
    public required string DisplayName { get; init; }
}

/// <summary>
/// Normalizes EditorIDs (and mesh folder segments) into ArmorSet keys for the EDID/mesh
/// fallback grouping stage. Strips creation-club prefixes, known set prefixes, and piece-type
/// suffixes; lowercases and keeps alphanumerics only. The same pipeline normalizes Outfit
/// EditorIDs without piece-type stripping (so a set split between an Outfit half and an
/// EDID-fallback half joins into one key).
/// </summary>
public static class KeyNormalizer
{
    /// <summary>
    /// Known leading set prefixes to strip (matched case-sensitively, longest first). "AA" is
    /// an author prefix used by some armor mods, "zzz" is the sort-last cosmetic prefix, and
    /// "ba_" strips the Bethesda Creation-Club author segment ("ccBGSSSE063-ba_elvenCuirass"
    /// -> "Elven"). "DLC2" is kept short (not "DLC2NordicCarved") so that
    /// "DLC2NordicCarvedGauntlets" normalizes to "NordicCarved" and joins the Outfit-driven
    /// "DLC2NordicCarved" set.
    /// </summary>
    private static readonly IReadOnlyList<string> SetPrefixes =
    [
        "AANord",
        "DLC2",
        "DLC1",
        "DLC0",
        "ba_",
        "zzz",
        "Clothes",
        "Clothing",
        "Armor",
        "AA",
    ];

    /// <summary>
    /// Piece-type/suffix tokens removed from the tail of an Armor EditorID (not from Outfit
    /// EditorIDs).
    /// </summary>
    private static readonly IReadOnlyList<string> PieceSuffixes =
    [
        "Gauntlets",
        "Bracers",
        "Cuirass",
        "Sandals",
        "Shoes",
        "Gloves",
        "Boots",
        "Helmet",
        "Hood",
        "Circlet",
        "Shield",
        "Amulet",
        "Ring",
        "Plate",
        "Robe",
        "Dress",
        "Crown",
        "Gem",
        "Tail",
        "Armor",
        "Clothes",
    ];

    /// <summary>
    /// Variant markers that map a piece-suffix to a normalizable tail even when concatenated
    /// directly (e.g. "IronCuirassAA" -> strip "AA" then "Cuirass").
    /// </summary>
    private static readonly IReadOnlyList<string> MarkerTokens =
    [
        "AA",
        "ba",
    ];

    /// <summary>
    /// Stop words stripped from the tail once piece suffixes are gone, so variant EDIDs such
    /// as "ClothesCollegeRobesNoHood" group with the base "College Robes" set.
    /// </summary>
    private static readonly IReadOnlyList<string> StopWords =
    [
        "No",
        "Yes",
    ];

    /// <summary>
    /// Normalizes an armor EditorID into a set key. The pipeline: strip a creation-club
    /// prefix through the first '-'/'_' separator, strip known set prefixes (longest first,
    /// repeated), strip the 'AA'/'ba' marker, then strip the piece-suffix (when
    /// <paramref name="stripPieceSuffix"/> is true). If a meaningful middle remains (length
    /// >= 2) it becomes the key; otherwise null is returned (caller falls to the mesh-folder
    /// fallback).
    /// </summary>
    public static NormalizedSetKey? NormalizeEditorId(string? editorId, bool stripPieceSuffix = true)
    {
        if (string.IsNullOrWhiteSpace(editorId))
        {
            return null;
        }

        var text = StripCcPrefix(editorId);
        text = StripPrefixes(text, SetPrefixes);
        text = StripMarker(text);

        if (stripPieceSuffix)
        {
            text = StripPieceVariant(text);
            text = StripPieceSuffix(text);
        }

        text = StripStopWord(text);

        return ToKey(text);
    }

    /// <summary>
    /// Normalizes an Outfit EditorID into a set key. Same pipeline as
    /// <see cref="NormalizeEditorId"/> but without piece-type stripping, so that the fallback
    /// half of a split-membership set (e.g. "NordicCarvedGauntlets" normalizes to
    /// "nordiccarved") joins the Outfit half ("NordicCarvedPlate" -> "nordiccarved").
    /// </summary>
    public static NormalizedSetKey? NormalizeOutfitEditorId(string? editorId)
    {
        return NormalizeEditorId(editorId, stripPieceSuffix: false);
    }

    /// <summary>
    /// Normalizes the armature model directory into a set key: takes the path segment that
    /// follows 'armor' or 'clothes' (e.g. 'meshes/armor/vigilant/cuirass.nif' -> 'vigilant'),
    /// stripping {male,female,_0,_1,_1st} suffixes; falls back to the last path segment of the
    /// meshes directory when no such marker precedes it.
    /// </summary>
    public static NormalizedSetKey? NormalizeMeshFolder(string? meshPath)
    {
        if (string.IsNullOrWhiteSpace(meshPath))
        {
            return null;
        }

        var parts = meshPath.Replace('\\', '/').Trim('/').Split('/');

        string candidate;
        var markerIdx = -1;
        for (var i = 0; i < parts.Length; i++)
        {
            if (string.Equals(parts[i], "armor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[i], "clothes", StringComparison.OrdinalIgnoreCase))
            {
                markerIdx = i;
            }
        }

        if (markerIdx >= 0 && markerIdx + 1 < parts.Length)
        {
            candidate = parts[markerIdx + 1];
        }
        else if (parts.Length >= 2)
        {
            candidate = parts[^2];
        }
        else
        {
            candidate = parts[^1];
        }

        var cleaned = StripSuffixTokens(candidate, ["male", "female", "_0", "_1", "_1st"], StringComparison.OrdinalIgnoreCase);

        if (cleaned.Length < 2)
        {
            return null;
        }

        return ToKey(cleaned);
    }

    private static string StripCcPrefix(string editorId)
    {
        var idx = editorId.IndexOfAny(['-', '_']);
        if (idx > 0 && editorId.StartsWith("cc", StringComparison.OrdinalIgnoreCase))
        {
            return editorId[(idx + 1)..];
        }

        return editorId;
    }

    private static string StripPrefixes(string text, IReadOnlyList<string> prefixes)
    {
        foreach (var sorted in prefixes.OrderByDescending(p => p.Length))
        {
            while (text.StartsWith(sorted, StringComparison.Ordinal) && text.Length >= sorted.Length)
            {
                text = text[sorted.Length..];
            }
        }

        return text;
    }

    private static string StripMarker(string text)
    {
        foreach (var marker in MarkerTokens)
        {
            if (text.EndsWith(marker, StringComparison.Ordinal) && text.Length > marker.Length)
            {
                return text[..^marker.Length];
            }
        }

        return text;
    }

    /// <summary>
    /// Drops a trailing single uppercase variant letter when a piece suffix follows it (e.g.
    /// "SteelBootsA" -> "SteelBoots"), so vanilla A/B piece alternates (Steel cuirass A/B,
    /// helmet A/B) share one set family instead of fragmenting into "steelbootsa" vs
    /// "steelcuirassa".
    /// </summary>
    private static string StripPieceVariant(string text)
    {
        if (text.Length <= 2 || !char.IsUpper(text[^1]))
        {
            return text;
        }

        var tail = text[..^1];
        foreach (var suffix in PieceSuffixes.OrderByDescending(s => s.Length))
        {
            if (tail.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return tail;
            }
        }

        return text;
    }

    private static string StripStopWord(string text)
    {
        foreach (var word in StopWords)
        {
            if (text.EndsWith(word, StringComparison.OrdinalIgnoreCase) && text.Length > word.Length)
            {
                return text[..^word.Length];
            }
        }

        return text;
    }

    private static string StripPieceSuffix(string text)
    {
        foreach (var suffix in PieceSuffixes.OrderByDescending(s => s.Length))
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && text.Length >= suffix.Length)
            {
                return text[..^suffix.Length];
            }
        }

        return text;
    }

    private static string StripSuffixTokens(string text, IReadOnlyList<string> tokens, StringComparison comparison)
    {
        foreach (var token in tokens.OrderByDescending(t => t.Length))
        {
            if (text.EndsWith(token, comparison) && text.Length > token.Length)
            {
                text = text[..^token.Length];
            }
        }

        return text;
    }

    private static NormalizedSetKey? ToKey(string text)
    {
        var cleaned = new string(text.Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length < 2)
        {
            return null;
        }

        var id = cleaned.ToLowerInvariant();
        var displayName = TitleCaseCamelCase(cleaned);

        return new NormalizedSetKey { Id = id, DisplayName = displayName };
    }

    private static string TitleCaseCamelCase(string camel)
    {
        var result = new System.Text.StringBuilder(camel.Length + 4);
        for (var i = 0; i < camel.Length; i++)
        {
            var ch = camel[i];
            if (i == 0)
            {
                result.Append(char.ToUpperInvariant(ch));
            }
            else if (char.IsUpper(ch) && char.IsLower(camel[i - 1]))
            {
                result.Append(' ');
                result.Append(ch);
            }
            else
            {
                result.Append(ch);
            }
        }

        return result.ToString();
    }
}
