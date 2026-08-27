using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Scanner;

public sealed class RecordIndex
{
    private static readonly IReadOnlySet<string> ArmorWeightKeywords =
        new HashSet<string>(StringComparer.Ordinal) { "ArmorHeavy", "ArmorLight", "ArmorClothing" };

    private readonly Dictionary<FormKey, IArmorGetter> _armor;
    private readonly Dictionary<FormKey, IArmorAddonGetter> _arma;
    private readonly Dictionary<FormKey, IKeywordGetter> _keyword;
    private readonly Dictionary<FormKey, ITextureSetGetter> _textureSet;
    private readonly Dictionary<FormKey, IRaceGetter> _race;
    private readonly Dictionary<FormKey, IOutfitGetter> _outfit;
    private readonly Dictionary<FormKey, HashSet<FormKey>> _outfitsByArmor;

    private RecordIndex(
        Dictionary<FormKey, IArmorGetter> armor,
        Dictionary<FormKey, IArmorAddonGetter> arma,
        Dictionary<FormKey, IKeywordGetter> keyword,
        Dictionary<FormKey, ITextureSetGetter> textureSet,
        Dictionary<FormKey, IRaceGetter> race,
        Dictionary<FormKey, IOutfitGetter> outfit,
        Dictionary<FormKey, HashSet<FormKey>> outfitsByArmor)
    {
        _armor = armor;
        _arma = arma;
        _keyword = keyword;
        _textureSet = textureSet;
        _race = race;
        _outfit = outfit;
        _outfitsByArmor = outfitsByArmor;
    }

    public int ArmorCount => _armor.Count;

    public int ArmorAddonCount => _arma.Count;

    public int KeywordCount => _keyword.Count;

    public int TextureSetCount => _textureSet.Count;

    public int RaceCount => _race.Count;

    public int OutfitCount => _outfit.Count;

    public IEnumerable<IArmorGetter> EnumerateArmor() => _armor.Values;

    public IEnumerable<IArmorAddonGetter> EnumerateArmorAddons() => _arma.Values;

    public bool TryResolveRace(FormKey key, out IRaceGetter record)
    {
        if (_race.TryGetValue(key, out var race))
        {
            record = race;
            return true;
        }

        record = null!;
        return false;
    }

    public bool TryResolveOutfit(FormKey key, out IOutfitGetter record)
    {
        if (_outfit.TryGetValue(key, out var outfit))
        {
            record = outfit;
            return true;
        }

        record = null!;
        return false;
    }

    public IReadOnlySet<FormKey> OutfitsForArmor(FormKey armorKey)
    {
        return _outfitsByArmor.TryGetValue(armorKey, out var set)
            ? set
            : new HashSet<FormKey>();
    }

    public bool TryResolveArmor(FormKey key, out IArmorGetter record)
    {
        if (_armor.TryGetValue(key, out var armor))
        {
            record = armor;
            return true;
        }

        record = null!;
        return false;
    }

    public bool TryResolveArmorAddon(FormKey key, out IArmorAddonGetter record)
    {
        if (_arma.TryGetValue(key, out var arma))
        {
            record = arma;
            return true;
        }

        record = null!;
        return false;
    }

    public bool TryResolveKeyword(FormKey key, out IKeywordGetter record)
    {
        if (_keyword.TryGetValue(key, out var keyword))
        {
            record = keyword;
            return true;
        }

        record = null!;
        return false;
    }

    public bool TryResolveTextureSet(FormKey key, out ITextureSetGetter record)
    {
        if (_textureSet.TryGetValue(key, out var textureSet))
        {
            record = textureSet;
            return true;
        }

        record = null!;
        return false;
    }

    public static RecordIndex Build(
        IReadOnlyList<LoadedMod> orderedMods,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken = default)
    {
        var armor = new Dictionary<FormKey, IArmorGetter>();
        var arma = new Dictionary<FormKey, IArmorAddonGetter>();
        var keyword = new Dictionary<FormKey, IKeywordGetter>();
        var textureSet = new Dictionary<FormKey, ITextureSetGetter>();
        var referencedTextureSets = new HashSet<FormKey>();
        var referencedRaces = new HashSet<FormKey>();

        foreach (var mod in orderedMods)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (var entry in mod.Overlay.Armors.RecordCache)
                {
                    armor[entry.Key] = entry.Value;
                }
            }
            catch (Exception ex)
            {
                warnings.Add(IndexWarning(mod, ex));
            }

            try
            {
                foreach (var entry in mod.Overlay.ArmorAddons.RecordCache)
                {
                    arma[entry.Key] = entry.Value;
                    CollectSkinTextureSetKeys(entry.Value, referencedTextureSets);
                    CollectRaceKey(entry.Value, referencedRaces);
                }
            }
            catch (Exception ex)
            {
                warnings.Add(IndexWarning(mod, ex));
            }
        }

        foreach (var mod in orderedMods)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (var entry in mod.Overlay.Keywords.RecordCache)
                {
                    var editorId = entry.Value.EditorID;
                    if (editorId is not null && ArmorWeightKeywords.Contains(editorId))
                    {
                        keyword[entry.Key] = entry.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add(IndexWarning(mod, ex));
            }

            try
            {
                foreach (var entry in mod.Overlay.TextureSets.RecordCache)
                {
                    if (referencedTextureSets.Contains(entry.Key))
                    {
                        textureSet[entry.Key] = entry.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add(IndexWarning(mod, ex));
            }
        }

        var race = new Dictionary<FormKey, IRaceGetter>();
        var outfit = new Dictionary<FormKey, IOutfitGetter>();
        var outfitsByArmor = new Dictionary<FormKey, HashSet<FormKey>>();

        foreach (var mod in orderedMods)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (var entry in mod.Overlay.Races.RecordCache)
                {
                    if (referencedRaces.Contains(entry.Key))
                    {
                        race[entry.Key] = entry.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add(IndexWarning(mod, ex));
            }

            try
            {
                foreach (var entry in mod.Overlay.Outfits.RecordCache)
                {
                    outfit[entry.Key] = entry.Value;
                    if (entry.Value.Items is null)
                    {
                        continue;
                    }

                    foreach (var item in entry.Value.Items)
                    {
                        var memberArmorKey = item.FormKey;
                        if (memberArmorKey.IsNull)
                        {
                            continue;
                        }

                        if (!outfitsByArmor.TryGetValue(memberArmorKey, out var members))
                        {
                            members = new HashSet<FormKey>();
                            outfitsByArmor[memberArmorKey] = members;
                        }

                        members.Add(entry.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add(IndexWarning(mod, ex));
            }
        }

        return new RecordIndex(armor, arma, keyword, textureSet, race, outfit, outfitsByArmor);
    }

    private static void CollectRaceKey(IArmorAddonGetter arma, HashSet<FormKey> referencedRaces)
    {
        var raceLink = arma.Race;
        if (raceLink is not null && !raceLink.FormKey.IsNull)
        {
            referencedRaces.Add(raceLink.FormKey);
        }
    }

    private static void CollectSkinTextureSetKeys(IArmorAddonGetter arma, HashSet<FormKey> referencedTextureSets)
    {
        var skinTexture = arma.SkinTexture;
        if (skinTexture is null)
        {
            return;
        }

        AddIfSet(skinTexture.Male, referencedTextureSets);
        AddIfSet(skinTexture.Female, referencedTextureSets);
    }

    private static void AddIfSet(IFormLinkNullableGetter<ITextureSetGetter> link, HashSet<FormKey> referencedTextureSets)
    {
        if (!link.FormKey.IsNull)
        {
            referencedTextureSets.Add(link.FormKey);
        }
    }

    private static ScanWarning IndexWarning(LoadedMod mod, Exception ex)
    {
        return new ScanWarning(
            $"Records of plugin '{mod.AbsolutePath}' could not be read into the record index and were skipped: {ex.Message}");
    }
}