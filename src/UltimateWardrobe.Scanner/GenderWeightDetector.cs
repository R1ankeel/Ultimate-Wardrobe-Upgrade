using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Scanner;

/// <summary>
/// Derives the weight class from armor weight keywords (Sprint 1.4.1) and the gender options
/// from explicit markers and ARMA world-model / weight-slider signals (Sprint 1.4.2).
/// </summary>
public static class GenderWeightDetector
{
    /// <summary>
    /// EditorID tokens that mark a record as explicitly gender-specific (checked longest-first,
    /// case-insensitive). Such markers override every ARMA slide/model signal.
    /// </summary>
    private static readonly IReadOnlyList<(string Token, Gender Gender)> GenderEditorIdTokens =
    [
        ("_female", Gender.Female),
        ("-female", Gender.Female),
        ("_male", Gender.Male),
        ("-male", Gender.Male),
        ("female", Gender.Female),
        ("male", Gender.Male),
        ("_f", Gender.Female),
        ("-f", Gender.Female),
        ("_m", Gender.Male),
        ("-m", Gender.Male),
    ];

    /// <summary>
    /// Weight-class keyword EditorIDs, highest priority first (Sprint 1.4.1).
    /// </summary>
    private const string HeavyKeyword = "ArmorHeavy";

    private const string LightKeyword = "ArmorLight";

    private const string ClothingKeyword = "ArmorClothing";

    /// <summary>
    /// Resolves the weight class of an armor from its keywords. Priority: heavy keyword wins,
    /// then light, then clothing (Sprint 1.4.1). When no armor-weight keyword is present the
    /// BOD2 <see cref="ArmorType"/> is used as a bonus signal; still none -> <see cref="WeightClass.Any"/>.
    /// </summary>
    public static WeightClass DetectWeight(CorrelatedArmor armor, RecordIndex index)
    {
        var keywords = armor.Armor.Keywords;
        if (keywords is not null)
        {
            WeightClass? fromKeyword = null;
            foreach (var link in keywords)
            {
                if (link is null || link.FormKey.IsNull || !index.TryResolveKeyword(link.FormKey, out var keyword))
                {
                    continue;
                }

                switch (keyword.EditorID)
                {
                    case HeavyKeyword when fromKeyword is not WeightClass.Heavy:
                        fromKeyword = WeightClass.Heavy;
                        break;
                    case LightKeyword when fromKeyword is null or WeightClass.Light:
                        fromKeyword = WeightClass.Light;
                        break;
                    case ClothingKeyword when fromKeyword is null:
                        fromKeyword = WeightClass.Clothing;
                        break;
                }
            }

            if (fromKeyword is not null)
            {
                return fromKeyword.Value;
            }
        }

        return ArmorTypeToWeight(armor.Armor.BodyTemplate?.ArmorType);
    }

    /// <summary>
    /// Detects the gender options of an armor. Resolution order (Sprint 1.4.2):
    /// 1) explicit gender-specific ARMO markers (EditorID suffix, then mesh-path folder), else
    /// 2) ARMA world-model / weight-slider signals (frozen in Sprint 1.0.5): both genders
    ///    signaled -> Male + Female; one -> that gender; else
    /// 3) a playable-race hint from the ARMA race EditorID (Female/Male marker), else
    /// 4) <see cref="Gender.Unisex"/> with a <see cref="ScanWarning"/>.
    /// </summary>
    public static IReadOnlyList<Gender> DetectGenders(CorrelatedArmor armor, RecordIndex index, List<ScanWarning> warnings)
    {
        var explicitGender = ExplicitFromEditorId(armor.EditorId) ?? ExplicitFromMeshPath(armor.MeshPath);
        if (explicitGender is not null)
        {
            return new[] { explicitGender.Value };
        }

        var signals = ArmaSignals(armor.FirstAddon);
        if (signals.Male && signals.Female)
        {
            return new[] { Gender.Male, Gender.Female };
        }

        if (signals.Male)
        {
            return new[] { Gender.Male };
        }

        if (signals.Female)
        {
            return new[] { Gender.Female };
        }

        var raceHint = RaceGenderHint(ResolveRace(armor, index));
        if (raceHint is not null)
        {
            return new[] { raceHint.Value };
        }

        warnings.Add(new ScanWarning(
            $"Armor '{armor.EditorId}' (FormId {armor.FormId:X}) carries no gender signal on its armature " +
            "(no world model and no weight-slider morph data for either gender); treating it as Unisex.",
            armor.EditorId));
        return new[] { Gender.Unisex };
    }

    /// <summary>
    /// Returns an explicit gender from an EditorID suffix ('_male', 'F', 'Female', ...), or null.
    /// </summary>
    public static Gender? ExplicitFromEditorId(string? editorId)
    {
        if (string.IsNullOrWhiteSpace(editorId))
        {
            return null;
        }

        foreach (var entry in GenderEditorIdTokens.OrderByDescending(t => t.Token.Length))
        {
            if (editorId.EndsWith(entry.Token, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Gender;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns an explicit gender from a mesh path folder segment, or null when the path
    /// contains no (or both) gender markers. The EditorID signal wins when both are present.
    /// </summary>
    public static Gender? ExplicitFromMeshPath(string? meshPath)
    {
        if (string.IsNullOrWhiteSpace(meshPath))
        {
            return null;
        }

        var hasFemale = false;
        var hasMale = false;
        foreach (var segment in meshPath.Replace('\\', '/').Split('/'))
        {
            if (string.Equals(segment, "female", StringComparison.OrdinalIgnoreCase))
            {
                hasFemale = true;
            }
            else if (string.Equals(segment, "male", StringComparison.OrdinalIgnoreCase))
            {
                hasMale = true;
            }
        }

        if (hasFemale && !hasMale)
        {
            return Gender.Female;
        }

        if (hasMale && !hasFemale)
        {
            return Gender.Male;
        }

        return null;
    }

    /// <summary>
    /// Race-based gender hint: a resolvable ARMA race whose EditorID carries a Female/Male
    /// marker implies that single gender. Only consulted when model/slider signals are absent.
    /// </summary>
    public static Gender? RaceGenderHint(IRaceGetter? race)
    {
        var editorId = race?.EditorID;
        if (editorId is null)
        {
            return null;
        }

        if (editorId.Contains("female", StringComparison.OrdinalIgnoreCase))
        {
            return Gender.Female;
        }

        if (editorId.Contains("male", StringComparison.OrdinalIgnoreCase))
        {
            return Gender.Male;
        }

        return null;
    }

    private static IRaceGetter? ResolveRace(CorrelatedArmor armor, RecordIndex index)
    {
        if (armor.RaceLink is null || armor.RaceLink.FormKey.IsNull
            || !index.TryResolveRace(armor.RaceLink.FormKey, out var race))
        {
            return null;
        }

        return race;
    }

    private static (bool Male, bool Female) ArmaSignals(IArmorAddonGetter? addon)
    {
        if (addon is null)
        {
            return (false, false);
        }

        var maleModel = addon.WorldModel?.Male?.File is { } maleFile && !maleFile.IsNull;
        var femaleModel = addon.WorldModel?.Female?.File is { } femaleFile && !femaleFile.IsNull;
        var maleSlider = addon.WeightSliderEnabled?.Male == true;
        var femaleSlider = addon.WeightSliderEnabled?.Female == true;

        return (maleModel || maleSlider, femaleModel || femaleSlider);
    }

    private static WeightClass ArmorTypeToWeight(ArmorType? armorType)
    {
        return armorType switch
        {
            ArmorType.HeavyArmor => WeightClass.Heavy,
            ArmorType.LightArmor => WeightClass.Light,
            ArmorType.Clothing => WeightClass.Clothing,
            _ => WeightClass.Any,
        };
    }
}