using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using Noggog;

namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// F4 synthetic clothing universe - mirrors the vanilla clothing enumeration from the spec
/// (34 Clothes + 14 Robes + 15 Hoods + 33 Shoes + 2 Gloves = 98) with distinct FormIDs and
/// BOD2 slots. Each item gets a distinct mesh subfolder so KeyNormalizer mesh fallback cannot
/// collapse them into a single megaset via the bare "clothes" key. This locks the F4 invariant
/// that Belted Tunic etc. remain separate ArmorSets and that jewelry/enchanted filtering is not
/// over-triggering on clothing.
/// </summary>
internal static class SyntheticClothingUniverse
{
    public const string FileName = "ClothingUniverse.esp";
    public static ModKey Mod => ModKey.FromName("ClothingUniverse", ModType.Plugin);

    public static FormKey ClothingKeywordKey => new(Mod, 0xA00);
    public static FormKey HeavyKeywordKey => new(Mod, 0xA01);

    // Reserve FormKey range 0x1000 + i for 98 items (avoids lower range 0x800 restriction)
    private static FormKey Key(int i) => new(Mod, (uint)(0x1000 + i));
    private static FormKey AddonKey(int i) => new(Mod, (uint)(0x2000 + i));

    private static readonly (string EditorId, string MeshFolder, BipedObjectFlag Flags, ArmorType Type)[] Clothes =
    {
        ("ClothesBeltedTunic", "clothes/beltedtunic", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesBlacksmithApronA", "clothes/blacksmithapron_a", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesBlacksmithApronB", "clothes/blacksmithapron_b", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesChefTunic", "clothes/cheftunic", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFarmClothes01", "clothes/farmclothes01", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFarmClothes02", "clothes/farmclothes02", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFarmClothes03", "clothes/farmclothes03", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFarmClothes04", "clothes/farmclothes04", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFarmClothes05", "clothes/farmclothes05", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFarmClothes06", "clothes/farmclothes06", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFarmClothes07", "clothes/farmclothes07", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFarmClothes08", "clothes/farmclothes08", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFarmClothes09", "clothes/farmclothes09", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFarmClothes10", "clothes/farmclothes10", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFarmClothes11", "clothes/farmclothes11", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesCollegeRobesNoHoodA", "clothes/collegerobes_a", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFineClothes01", "clothes/fineclothes01", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFineClothes02", "clothes/fineclothes02", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFineClothes03", "clothes/fineclothes03", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFineClothes04", "clothes/fineclothes04", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesFurTrimmedCloak", "clothes/furtrimmedcloak", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesHammerfellGarb", "clothes/hammerfellgarb", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesMinerClothes01", "clothes/minerclothes01", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesMinerClothes02", "clothes/minerclothes02", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesMournerClothes", "clothes/mournerclothes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesNobleClothes", "clothes/nobleclothes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesPartyClothes", "clothes/partyclothes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesRadiantRaimentFineClothes", "clothes/radiantraiment", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesRaggedTrousersA", "clothes/raggedtrousers_a", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesRaggedTrousersB", "clothes/raggedtrousers_b", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesRedguardClothes", "clothes/redguardclothes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesRoughspunTunic", "clothes/roughspuntunic", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesTavernClothes", "clothes/tavernclothes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesWeddingDress", "clothes/weddingdress", BipedObjectFlag.Body, ArmorType.Clothing),
    };

    private static readonly (string EditorId, string MeshFolder, BipedObjectFlag Flags, ArmorType Type)[] Robes =
    {
        ("ClothesBlackRobes", "clothes/blackrobes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesBlueRobes", "clothes/bluerobes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesCollegeRobesA", "clothes/collegerobes_b", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesCollegeRobesB", "clothes/collegerobes_c", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesEmperorRobes", "clothes/emperorrobes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesGreenRobes", "clothes/greenrobes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesHoodedBlackRobes", "clothes/hoodedblackrobes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesHoodedBlueRobes", "clothes/hoodedbluerobes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesHoodedMonkRobes", "clothes/hoodedmonkrobes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesMageRobes", "clothes/magerobes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesMonkRobes", "clothes/monkrobes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesRaggedRobes", "clothes/raggedrobes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesRedRobes", "clothes/redrobes", BipedObjectFlag.Body, ArmorType.Clothing),
        ("ClothesVaerminaRobes", "clothes/vaerminarobes", BipedObjectFlag.Body, ArmorType.Clothing),
    };

    private static readonly (string EditorId, string MeshFolder, BipedObjectFlag Flags, ArmorType Type)[] Hoods =
    {
        ("ClothesAlikrHood", "clothes/alikrhood", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesChefHat", "clothes/chefhat", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesCowl", "clothes/cowl", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesFineHat", "clothes/finehat", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesHatA", "clothes/hata", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesHatB", "clothes/hatb", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesHatC", "clothes/hatc", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesHatD", "clothes/hatd", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesMageHoodA", "clothes/magehooda", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesMageHoodB", "clothes/magehoodb", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesMageHoodC", "clothes/magehoodc", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesMournerHat", "clothes/mournerhat", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesRaggedCap", "clothes/raggedcap", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesRedguardHood", "clothes/redguardhood", BipedObjectFlag.Hair, ArmorType.Clothing),
        ("ClothesTemplePriestHood", "clothes/templepriesthood", BipedObjectFlag.Hair, ArmorType.Clothing),
    };

    private static readonly (string EditorId, string MeshFolder, BipedObjectFlag Flags, ArmorType Type)[] Shoes =
    {
        ("ClothesBootsA", "clothes/bootsa", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesBootsB", "clothes/bootsb", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesBootsC", "clothes/bootsc", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesBootsD", "clothes/bootsd", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesBootsE", "clothes/bootse", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesBootsF", "clothes/bootsf", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesBootsG", "clothes/bootsg", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesBootsH", "clothes/bootsh", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesBootsI", "clothes/bootsi", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesBootsJ", "clothes/bootsj", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesBootsK", "clothes/bootsk", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesCuffedBoots", "clothes/cuffedboots", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesCultistBoots", "clothes/cultistboots", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesDunmerShoes", "clothes/dunmershoes", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesFineBootsA", "clothes/finebootsa", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesFineBootsB", "clothes/finebootsb", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesFineBootsC", "clothes/finebootsc", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesFootwrapsA", "clothes/footwrapsa", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesFootwrapsB", "clothes/footwrapsb", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesFurLinedBoots", "clothes/furlinedboots", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesMythicDawnBoots", "clothes/mythicdawnboots", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesPartyBoots", "clothes/partyboots", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesPleatedShoes", "clothes/pleatedshoes", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesRaggedBoots", "clothes/raggedboots", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesRedguardBoots", "clothes/redguardboots", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesShoesA", "clothes/shoesa", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesShoesB", "clothes/shoesb", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesShoesC", "clothes/shoesc", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesShoesD", "clothes/shoesd", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesTemplePriestBoots", "clothes/templepriestboots", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesThalmorBoots", "clothes/thalmorboots", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesWeddingSandals", "clothes/weddingsandals", BipedObjectFlag.Feet, ArmorType.Clothing),
        ("ClothesBootsL", "clothes/bootsl", BipedObjectFlag.Feet, ArmorType.Clothing),
    };

    private static readonly (string EditorId, string MeshFolder, BipedObjectFlag Flags, ArmorType Type)[] Gloves =
    {
        ("ClothesGlovesA", "clothes/glovesa", BipedObjectFlag.Hands, ArmorType.Clothing),
        ("ClothesThalmorGloves", "clothes/thalmorgloves", BipedObjectFlag.Hands, ArmorType.Clothing),
    };

    public static string Write(string directory)
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);
        mod.Keywords.Add(new Keyword(ClothingKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorClothing" });
        mod.Keywords.Add(new Keyword(HeavyKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorHeavy" });

        var all = Clothes.Concat(Robes).Concat(Hoods).Concat(Shoes).Concat(Gloves).ToArray();
        for (int i = 0; i < all.Length; i++)
        {
            var (editorId, meshFolder, flags, type) = all[i];
            AddArmor(mod, Key(i), AddonKey(i), editorId, editorId, flags, type, meshFolder + "/mesh.nif");
        }

        var path = Path.Combine(directory, FileName);
        mod.WriteToBinary(path);
        return path;
    }

    private static void AddArmor(SkyrimMod mod, FormKey armorKey, FormKey addonKey, string editorId, string name, BipedObjectFlag flags, ArmorType armorType, string meshPath)
    {
        var keywordKey = armorType == ArmorType.Clothing ? ClothingKeywordKey : HeavyKeywordKey;
        var bodyTemplate = new BodyTemplate { FirstPersonFlags = flags, ArmorType = armorType };
        var fullMesh = "meshes/" + meshPath;
        var addon = new ArmorAddon(addonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId + "AA",
            BodyTemplate = bodyTemplate,
            WorldModel = new GenderedItem<Model?>(MakeModel(fullMesh), MakeModel(fullMesh)),
        };
        mod.ArmorAddons.Add(addon);
        var armor = new Armor(armorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId,
            Name = name,
            BodyTemplate = bodyTemplate,
            Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>> { new FormLink<IKeywordGetter>(keywordKey) },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(addonKey) },
        };
        mod.Armors.Add(armor);
    }

    private static Model MakeModel(string path)
    {
        var model = new Model();
        var file = new AssetLink<SkyrimModelAssetType>();
        file.TrySetPath(path);
        model.File = file;
        return model;
    }
}
