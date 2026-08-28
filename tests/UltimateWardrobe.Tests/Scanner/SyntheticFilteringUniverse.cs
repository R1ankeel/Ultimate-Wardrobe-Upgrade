using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using Noggog;

namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// Sprint 6.8 filter universe (manual-testing bugs 1 and 2): a standalone plugin whose records exercise
/// the jewelry skip (ring, amulet), the keep-path (circlet - a head slot, not jewelry), and the
/// vanilla-enchantment name-suffix skip (single-word, multi-word, and &amp;-combined phrases, plus a
/// case-insensitive match), while a plain kit stays grouped. Kept separate from the golden MiniUniverse
/// and the Sprint 1.3 GroupingUniverse so their committed expectations stay untouched.
/// </summary>
internal static class SyntheticFilteringUniverse
{
    public const string FileName = "FilteringUniverse.esp";

    public static ModKey Mod => ModKey.FromName("FilteringUniverse", ModType.Plugin);

    public static FormKey HeavyKeywordKey => new(Mod, 0xF00);

    public static FormKey ClothingKeywordKey => new(Mod, 0xF01);

    public static FormKey RingKey => new(Mod, 0xF10);

    public static FormKey RingAddonKey => new(Mod, 0xF11);

    public static FormKey AmuletKey => new(Mod, 0xF12);

    public static FormKey AmuletAddonKey => new(Mod, 0xF13);

    public static FormKey CircletKey => new(Mod, 0xF14);

    public static FormKey CircletAddonKey => new(Mod, 0xF15);

    public static FormKey MuffleKey => new(Mod, 0xF20);

    public static FormKey MuffleAddonKey => new(Mod, 0xF21);

    public static FormKey OneHandedKey => new(Mod, 0xF22);

    public static FormKey OneHandedAddonKey => new(Mod, 0xF23);

    public static FormKey DualRegenKey => new(Mod, 0xF24);

    public static FormKey DualRegenAddonKey => new(Mod, 0xF25);

    public static FormKey LowercaseFireKey => new(Mod, 0xF26);

    public static FormKey LowercaseFireAddonKey => new(Mod, 0xF27);

    public static FormKey PlainCuirassKey => new(Mod, 0xF30);

    public static FormKey PlainCuirassAddonKey => new(Mod, 0xF31);

    public static string Write(string directory)
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);

        mod.Keywords.Add(new Keyword(HeavyKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorHeavy" });
        mod.Keywords.Add(new Keyword(ClothingKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorClothing" });

        AddArmor(mod, RingKey, RingAddonKey, "JewelRing", "Silver Ring",
            BipedObjectFlag.Ring, ArmorType.Clothing);
        AddArmor(mod, AmuletKey, AmuletAddonKey, "JewelAmulet", "Silver Necklace",
            BipedObjectFlag.Amulet, ArmorType.Clothing);
        AddArmor(mod, CircletKey, CircletAddonKey, "JewelCirclet", "Silver Circlet",
            BipedObjectFlag.Circlet, ArmorType.Clothing);

        AddArmor(mod, MuffleKey, MuffleAddonKey, "EnchMuffleBoots", "Steel Boots of Muffle",
            BipedObjectFlag.Feet, ArmorType.HeavyArmor);
        AddArmor(mod, OneHandedKey, OneHandedAddonKey, "EnchOneHandedCuirass", "Steel Cuirass of Major One-Handed",
            BipedObjectFlag.Body, ArmorType.HeavyArmor);
        AddArmor(mod, DualRegenKey, DualRegenAddonKey, "EnchDualRegenHelmet", "Mage Helm of Alteration & Magicka Regen",
            BipedObjectFlag.Head, ArmorType.Clothing);
        AddArmor(mod, LowercaseFireKey, LowercaseFireAddonKey, "EnchLowercaseFireGauntlets", "Iron Gauntlets of fire",
            BipedObjectFlag.Hands, ArmorType.HeavyArmor);

        AddArmor(mod, PlainCuirassKey, PlainCuirassAddonKey, "PlainHeavyCuirass", "Steel Cuirass",
            BipedObjectFlag.Body, ArmorType.HeavyArmor);

        var path = Path.Combine(directory, FileName);
        mod.WriteToBinary(path);
        return path;
    }

    private static void AddArmor(
        SkyrimMod mod,
        FormKey armorKey,
        FormKey addonKey,
        string editorId,
        string name,
        BipedObjectFlag flags,
        ArmorType armorType)
    {
        var keywordKey = armorType switch
        {
            ArmorType.HeavyArmor => HeavyKeywordKey,
            ArmorType.LightArmor => HeavyKeywordKey,
            _ => ClothingKeywordKey,
        };

        var bodyTemplate = new BodyTemplate { FirstPersonFlags = flags, ArmorType = armorType };

        var addon = new ArmorAddon(addonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId + "AA",
            BodyTemplate = bodyTemplate,
            WorldModel = new GenderedItem<Model?>(MakeModel(editorId + ".nif"), MakeModel(editorId + ".nif")),
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
        file.TrySetPath("meshes/armor/" + path);
        model.File = file;
        return model;
    }
}