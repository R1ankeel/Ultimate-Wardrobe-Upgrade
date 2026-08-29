namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Slot normalizer for F2 - bridges Branch-1 frozen slots ("32 Body", "31 Hair", "33 Hands", ...)
/// and Branch-2 word slots ("Cuirass", "Gauntlets", "Boots", ...) into a single canonical id.
/// Weight is already ignored (F1); this normalizer ensures "any vanilla item may be replaced by any
/// donor item sharing its biped slot" works for mesh-only donors and for mixed-layout donors.
/// </summary>
/// <remarks>
/// Canonical ids (case-sensitive, TitleCase):
/// - Body: Body (Cuirass, Armor, Clothes, Robe, Robes, Dress)
/// - Hands: Hands (Gauntlets, Gloves)
/// - Forearms: Forearms (Bracers) - family HandsFamily = Hands + Forearms (fallback)
/// - Feet: Feet (Boots, Shoes, Sandals) - family FeetFamily = Feet + Calves
/// - Head: Head (Helmet)
/// - Hair: Hair (Hood)
/// - LongHair: LongHair
/// - Circlet: Circlet (Crown, Circlet)
/// - Shield: Shield
/// - Ears: Ears
/// - Tail: Tail
/// - Amulet: Amulet
/// - Ring: Ring
/// - Calves: Calves
/// - Other: non-armor word or empty - treated as no match
/// HeadFamily = Head + Hair + LongHair + Circlet + Ears (helmet/hood/circlet interchangeable)
/// </remarks>
public static class SlotNormalizer
{
    private static readonly Dictionary<string, string> WordToCanonical = new(StringComparer.OrdinalIgnoreCase)
    {
        // Body family
        ["Body"] = "Body",
        ["Cuirass"] = "Body",
        ["Armor"] = "Body",
        ["Clothes"] = "Body",
        ["Robe"] = "Body",
        ["Robes"] = "Body",
        ["Dress"] = "Body",
        ["Skin"] = "Body",

        // Hands / Forearms
        ["Hands"] = "Hands",
        ["Gauntlets"] = "Hands",
        ["Gloves"] = "Hands",
        ["Forearms"] = "Forearms",
        ["Bracers"] = "Forearms",

        // Feet
        ["Feet"] = "Feet",
        ["Boots"] = "Feet",
        ["Shoes"] = "Feet",
        ["Sandals"] = "Feet",
        ["Calves"] = "Calves",

        // Head family
        ["Head"] = "Head",
        ["Helmet"] = "Head",
        ["Hair"] = "Hair",
        ["Hood"] = "Hair",
        ["LongHair"] = "LongHair",
        ["Circlet"] = "Circlet",
        ["Crown"] = "Circlet",
        ["Ears"] = "Ears",

        // Shield etc
        ["Shield"] = "Shield",
        ["Tail"] = "Tail",
        ["Amulet"] = "Amulet",
        ["Ring"] = "Ring",
    };

    private static readonly HashSet<string> HeadFamily = new(StringComparer.Ordinal)
    {
        "Head", "Hair", "LongHair", "Circlet", "Ears",
    };

    private static readonly HashSet<string> HandsFamily = new(StringComparer.Ordinal)
    {
        "Hands", "Forearms",
    };

    private static readonly HashSet<string> FeetFamily = new(StringComparer.Ordinal)
    {
        "Feet", "Calves",
    };

    /// <summary>
    /// Canonicalizes a slot string - strips the frozen numeric prefix ("32 Body" -> "Body") and maps
    /// word synonyms ("Cuirass" -> "Body") to a single canonical id. Returns null for "Other" or unknown.
    /// </summary>
    public static string? CanonicalId(string? slot)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            return null;
        }

        var trimmed = slot.Trim();
        string wordPart;

        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx >= 0)
        {
            wordPart = trimmed[(spaceIdx + 1)..].Trim();
        }
        else
        {
            wordPart = trimmed;
        }

        if (string.IsNullOrWhiteSpace(wordPart))
        {
            return null;
        }

        if (string.Equals(wordPart, "Other", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (WordToCanonical.TryGetValue(wordPart, out var canonical))
        {
            return canonical;
        }

        // Unknown word - treat as no match (null) rather than raw word to avoid false positives
        return null;
    }

    /// <summary>
    /// Returns true when two slot strings refer to the same equip location, using exact canonical
    /// match first, then family fallback for Hands/Forearms, Feet/Calves and the Head family.
    /// "Other" never matches anything.
    /// </summary>
    public static bool AreCompatible(string? a, string? b)
    {
        var ca = CanonicalId(a);
        var cb = CanonicalId(b);
        if (ca is null || cb is null)
        {
            return false;
        }

        if (string.Equals(ca, cb, StringComparison.Ordinal))
        {
            return true;
        }

        // Family fallback
        if (HandsFamily.Contains(ca) && HandsFamily.Contains(cb))
        {
            return true;
        }

        if (FeetFamily.Contains(ca) && FeetFamily.Contains(cb))
        {
            return true;
        }

        if (HeadFamily.Contains(ca) && HeadFamily.Contains(cb))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Frozen slot string for a Branch-2 word slot - maps word synonyms to the frozen "NN Name" form
    /// used by Branch-1 (BipedSlotMapper). Kept as helper for MeshSetAssembler optional emission.
    /// </summary>
    public static string ToFrozenSlot(string? pieceWord)
    {
        if (string.IsNullOrWhiteSpace(pieceWord))
        {
            return "Other";
        }

        var canonical = CanonicalId(pieceWord);
        if (canonical is null)
        {
            return "Other";
        }

        return canonical switch
        {
            "Body" => "32 Body",
            "Hands" => "33 Hands",
            "Forearms" => "34 Forearms",
            "Feet" => "37 Feet",
            "Calves" => "38 Calves",
            "Head" => "30 Head",
            "Hair" => "31 Hair",
            "LongHair" => "41 LongHair",
            "Circlet" => "42 Circlet",
            "Shield" => "39 Shield",
            "Tail" => "40 Tail",
            "Ears" => "43 Ears",
            "Amulet" => "35 Amulet",
            "Ring" => "36 Ring",
            _ => "Other",
        };
    }
}
