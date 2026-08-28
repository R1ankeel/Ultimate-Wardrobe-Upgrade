namespace UltimateWardrobe.Scanner;

/// <summary>
/// Vanilla armor enchantment effect words (manual-testing bug 2). A vanilla-enchanted ARMO's display
/// name ends with the enchantment's effect phrase (for example "Steel Gauntlets of Muffle"). An item
/// whose name ends with one of these words - case-insensitive - is a generic enchanted variant of a
/// base kit and is dropped from the catalog. Multi-word phrases are matched longest-first so that a
/// phrase like "Alteration &amp; Magicka Regen" wins over the bare "Alteration" / "Magicka" suffix.
/// </summary>
public static class VanillaEnchantmentFilter
{
    private static readonly string[] EffectWords =
    [
        "Alteration & Magicka Regen",
        "Conjuration & Magicka Regen",
        "Destruction & Magicka Regen",
        "Illusion & Magicka Regen",
        "Restoration & Magicka Regen",
        "Empower Necromancy",
        "Waterbreathing",
        "Healing Regen",
        "Magicka Regen",
        "Stamina Regen",
        "Carry Weight",
        "Dark Moon",
        "Heavy Armor",
        "Light Armor",
        "Lockpicking",
        "One-Handed",
        "Two-Handed",
        "Alteration",
        "Conjuration",
        "Destruction",
        "Illusion",
        "Pickpocket",
        "Restoration",
        "Smithing",
        "Alchemy",
        "Archery",
        "Magicka",
        "Stamina",
        "Unarmed",
        "Disease",
        "Health",
        "Muffle",
        "Barter",
        "Block",
        "Fire",
        "Frost",
        "Magic",
        "Poison",
        "Shock",
        "Sneak",
    ];

    private static readonly string[] SortedLongestFirst =
        EffectWords.OrderByDescending(w => w.Length).ToArray();

    /// <summary>
    /// True when <paramref name="name"/> ends with one of the vanilla enchantment effect words
    /// (case-insensitive). Unknown/null names are never a match.
    /// </summary>
    public static bool EndsWithEnchantment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var word in SortedLongestFirst)
        {
            if (name.EndsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}