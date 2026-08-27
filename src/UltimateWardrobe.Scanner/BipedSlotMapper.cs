using Mutagen.Bethesda.Skyrim;

namespace UltimateWardrobe.Scanner;

/// <summary>
/// Maps the BOD2 <see cref="BipedObjectFlag"/> set of an ARMO to the frozen Piece.Slot string
/// format "{BODTnumber} {Name}" (decided in Sprint 1.0.5), e.g. "37 Feet", "32 Body".
/// </summary>
public static class BipedSlotMapper
{
    /// <summary>
    /// Frozen flag -> slot table (Sprint 1.0.5, plan section 4.5). Order is the canonical
    /// precedence: the FIRST flag in this order that is set within a record is its primary
    /// slot, and the order doubles as the piece ordering inside a variant.
    /// </summary>
    public static readonly IReadOnlyList<(BipedObjectFlag Flag, int Slot, string Name)> Table =
    [
        (BipedObjectFlag.Head, 30, "Head"),
        (BipedObjectFlag.Hair, 31, "Hair"),
        (BipedObjectFlag.Body, 32, "Body"),
        (BipedObjectFlag.Hands, 33, "Hands"),
        (BipedObjectFlag.Forearms, 34, "Forearms"),
        (BipedObjectFlag.Amulet, 35, "Amulet"),
        (BipedObjectFlag.Ring, 36, "Ring"),
        (BipedObjectFlag.Feet, 37, "Feet"),
        (BipedObjectFlag.Calves, 38, "Calves"),
        (BipedObjectFlag.Shield, 39, "Shield"),
        (BipedObjectFlag.Tail, 40, "Tail"),
        (BipedObjectFlag.LongHair, 41, "LongHair"),
        (BipedObjectFlag.Circlet, 42, "Circlet"),
        (BipedObjectFlag.Ears, 43, "Ears"),
    ];

    /// <summary>
    /// Returns the frozen slot string for the primary (first-set in table order) flag, or null
    /// when the record carries no recognizable slot (only e.g. Decapitate/DecapitateHead/FX01).
    /// </summary>
    public static string? ToSlotString(BipedObjectFlag flags)
    {
        var index = SlotIndex(flags);
        if (index == int.MaxValue)
        {
            return null;
        }

        var entry = Table[index];
        return $"{entry.Slot} {entry.Name}";
    }

    /// <summary>
    /// Canonical ordering index of the primary flag (for piece ordering and the 1.3 member
    /// ordering), or <see cref="int.MaxValue"/> when no table flag is set.
    /// </summary>
    public static int SlotIndex(BipedObjectFlag flags)
    {
        for (var i = 0; i < Table.Count; i++)
        {
            if (flags.HasFlag(Table[i].Flag))
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}