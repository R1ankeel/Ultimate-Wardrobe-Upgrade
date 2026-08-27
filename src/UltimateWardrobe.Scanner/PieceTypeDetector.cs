using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Scanner;

/// <summary>
/// Detects the piece type of an ARMO record from two signals: the EditorID suffix
/// (e.g. "IronGauntlets" -> "Gauntlets") and the BOD2 slot flags (e.g.
/// <see cref="BipedObjectFlag.Hands"/> -> "Gauntlets"). The EditorID signal wins;
/// the BOD2 slot is cross-checked, and conflicting evidence logs a warning but the
/// detected piece type is still returned.
/// </summary>
public static class PieceTypeDetector
{
    /// <summary>
    /// Suffix tokens that identify a piece type from its EditorID.
    /// </summary>
    public static readonly IReadOnlyList<string> EquipmentWords =
    [
        "Cuirass",
        "Gauntlets",
        "Boots",
        "Helmet",
        "Hood",
        "Shield",
        "Circlet",
        "Gloves",
        "Bracers",
        "Sandals",
        "Shoes",
        "Robe",
        "Robes",
        "Dress",
        "Crown",
        "Amulet",
        "Ring",
        "Tail",
        "Armor",
        "Clothes",
    ];

    /// <summary>
    /// Returns the piece-type word detected from the EditorID suffix (longest match first),
    /// or null when no known suffix is present.
    /// </summary>
    public static string? FromEditorId(string editorId)
    {
        if (string.IsNullOrWhiteSpace(editorId))
        {
            return null;
        }

        foreach (var word in EquipmentWords.OrderByDescending(w => w.Length))
        {
            if (editorId.EndsWith(word, StringComparison.Ordinal))
            {
                return word;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the canonical piece-type word for the primary BOD2 slot flag, or null when
    /// the record carries no recognizable slot.
    /// </summary>
    public static string? FromFlags(BipedObjectFlag flags)
    {
        return flags switch
        {
            _ when flags.HasFlag(BipedObjectFlag.Body) => "Cuirass",
            _ when flags.HasFlag(BipedObjectFlag.Hands) => "Gauntlets",
            _ when flags.HasFlag(BipedObjectFlag.Forearms) => "Bracers",
            _ when flags.HasFlag(BipedObjectFlag.Feet) => "Boots",
            _ when flags.HasFlag(BipedObjectFlag.Head) => flags.HasFlag(BipedObjectFlag.Circlet) ? "Circlet" : "Helmet",
            _ when flags.HasFlag(BipedObjectFlag.Shield) => "Shield",
            _ when flags.HasFlag(BipedObjectFlag.Circlet) => "Circlet",
            _ when flags.HasFlag(BipedObjectFlag.Hair) => "Hood",
            _ when flags.HasFlag(BipedObjectFlag.LongHair) => "Hood",
            _ => null,
        };
    }

    /// <summary>
    /// Returns a piece-type word for the given EditorId and BOD2 flags. The EditorID suffix
    /// is preferred; the BOD2 flags are used as a cross-check. A mismatch emits a
    /// <see cref="ScanWarning"/> but the detected type is still returned (never null for a
    /// valid slot).
    /// </summary>
    public static string? Detect(string editorId, BipedObjectFlag flags, List<ScanWarning> warnings)
    {
        var fromId = FromEditorId(editorId);
        var fromFlags = FromFlags(flags);

        if (fromId is not null && fromFlags is not null && !string.Equals(fromId, fromFlags, StringComparison.Ordinal))
        {
            warnings.Add(new ScanWarning(
                $"Armor '{editorId}' has EditorID piece-type '{fromId}' but BOD2 slot flags indicate '{fromFlags}'; " +
                "keeping the EditorID signal.",
                editorId));
        }

        return fromId ?? fromFlags;
    }
}
