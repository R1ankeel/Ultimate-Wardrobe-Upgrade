using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using Noggog;

namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// Sprint 6.8/6.9 filter universe (manual-testing bugs 1 and 2): a standalone plugin whose records exercise
/// the jewelry skip (ring, amulet), the keep-path (circlet - a head slot, not jewelry), the
/// vanilla-enchantment name-suffix skip (single-word, multi-word, and &amp;-combined phrases, plus a
/// case-insensitive match), the Sprint 6.9 shared-mesh skip (an Ench* variant reusing the base-kit
/// mesh is dropped while the base kit stays and a unique-mesh enchanted robe stays), while a plain
/// kit stays grouped. Kept separate from the golden MiniUniverse and the Sprint 1.3 GroupingUniverse
/// so their committed expectations stay untouched.
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

    public static FormKey SharedBaseBootsKey => new(Mod, 0xF40);

    public static FormKey SharedBaseBootsAddonKey => new(Mod, 0xF41);

    public static FormKey EnchVariantBootsKey => new(Mod, 0xF42);

    public static FormKey EnchVariantBootsAddonKey => new(Mod, 0xF43);

    public static FormKey EnchUniqueRobeKey => new(Mod, 0xF44);

    public static FormKey EnchUniqueRobeAddonKey => new(Mod, 0xF45);

    public static FormKey DlcEnchVariantBootsKey => new(Mod, 0xF46);

    public static FormKey DlcEnchVariantBootsAddonKey => new(Mod, 0xF47);

    public static FormKey SharedMageBootsKey => new(Mod, 0xF50);

    public static FormKey SharedMageBootsAddonKey => new(Mod, 0xF51);

    public static FormKey DlcEnchClothesVariantBootsKey => new(Mod, 0xF52);

    public static FormKey DlcEnchClothesVariantBootsAddonKey => new(Mod, 0xF53);

    public static FormKey WenchClothes01Key => new(Mod, 0xF60);

    public static FormKey WenchClothes01AddonKey => new(Mod, 0xF61);

    public static FormKey WenchClothes02Key => new(Mod, 0xF62);

    public static FormKey WenchClothes02AddonKey => new(Mod, 0xF63);

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

        // Sprint 6.9 mesh-sharing fixtures: the enchanted boots reuse the exact mesh of the plain
        // base-kit boots (variant duplicate, dropped), while the enchanted robe owns a unique mesh
        // and therefore stays in the catalog.
        AddArmor(mod, SharedBaseBootsKey, SharedBaseBootsAddonKey, "SharedBaseBoots", "Daedric Boots",
            BipedObjectFlag.Feet, ArmorType.HeavyArmor, meshName: "shared_daedric_boots.nif");
        AddArmor(mod, EnchVariantBootsKey, EnchVariantBootsAddonKey, "EnchVariantBoots", "Daedric Boots of Brawn",
            BipedObjectFlag.Feet, ArmorType.HeavyArmor, meshName: "shared_daedric_boots.nif");
        AddArmor(mod, EnchUniqueRobeKey, EnchUniqueRobeAddonKey, "EnchUniqueRobe", "Robes of Quickening",
            BipedObjectFlag.Body, ArmorType.Clothing, meshName: "unique_warlock_robe.nif");

        // Sprint 6.9 DLC-prefixed enchanted variant: the DLC names its variants with a DLC prefix
        // before "Ench" (e.g. DLC2EnchArmor...), which the former strict "Ench" prefix rule missed.
        // It reuses the same base-kit mesh and must be dropped just like the base-game Ench* variant.
        AddArmor(mod, DlcEnchVariantBootsKey, DlcEnchVariantBootsAddonKey,
            "DLC2EnchArmorSharedBaseBootsConjuration03", "DLC2 Ench Armor Daedric Boots Conjuration03",
            BipedObjectFlag.Feet, ArmorType.HeavyArmor, meshName: "shared_daedric_boots.nif");

        // Enchanted CLOTHING variants, not just armor: the DLC names those DLC*EnchClothes..., and
        // base-game mage robes are EnchClothes... Mage Boots/Mage Robes variants reuse the base-kit
        // garment mesh and must be dropped too.
        AddArmor(mod, SharedMageBootsKey, SharedMageBootsAddonKey, "SharedMageBoots", "Mage Boots",
            BipedObjectFlag.Feet, ArmorType.Clothing, meshName: "shared_mage_boots.nif");
        AddArmor(mod, DlcEnchClothesVariantBootsKey, DlcEnchClothesVariantBootsAddonKey,
            "DLC2EnchClothesMageBootsSneak02", "DLC2 Ench Clothes Mage Boots Sneak02",
            BipedObjectFlag.Feet, ArmorType.Clothing, meshName: "shared_mage_boots.nif");

        // Guard: "Ench" embedded in another word (Wench...) is NOT the enchantment marker. Two Wench
        // outfits sharing a mesh must BOTH stay - the substring match must not misidentify them.
        AddArmor(mod, WenchClothes01Key, WenchClothes01AddonKey, "ClothesWenchClothes01", "Wench Clothes01",
            BipedObjectFlag.Body, ArmorType.Clothing, meshName: "shared_wench_outfit.nif");
        AddArmor(mod, WenchClothes02Key, WenchClothes02AddonKey, "ClothesWenchClothes02", "Wench Clothes02",
            BipedObjectFlag.Body, ArmorType.Clothing, meshName: "shared_wench_outfit.nif");

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
        ArmorType armorType,
        string? meshName = null)
    {
        var keywordKey = armorType switch
        {
            ArmorType.HeavyArmor => HeavyKeywordKey,
            ArmorType.LightArmor => HeavyKeywordKey,
            _ => ClothingKeywordKey,
        };

        var bodyTemplate = new BodyTemplate { FirstPersonFlags = flags, ArmorType = armorType };

        var modelPath = "meshes/armor/" + (meshName ?? editorId + ".nif");

        var addon = new ArmorAddon(addonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId + "AA",
            BodyTemplate = bodyTemplate,
            WorldModel = new GenderedItem<Model?>(MakeModel(modelPath), MakeModel(modelPath)),
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