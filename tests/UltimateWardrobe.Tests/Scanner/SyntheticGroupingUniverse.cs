using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using Noggog;

namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// A standalone synthetic plugin exercising the Sprint 1.3 ArmorSet grouping pipeline: an
/// Outfit-driven set, a split-membership set, a pure-EDID-fallback set, a creature-skin record,
/// a vampire-race record, a multi-outfit armor, and one record per garbage skip reason.
/// </summary>
internal static class SyntheticGroupingUniverse
{
    public const string FileName = "GroupingUniverse.esp";

    public static ModKey Mod => ModKey.FromName("GroupingUniverse", ModType.Plugin);

    public static FormKey BoarRaceKey => new(Mod, 0x900);

    public static FormKey NordVampireRaceKey => new(Mod, 0x901);

    /// <summary>A race FormKey referenced by an ARMA but absent from the file set (unresolvable).</summary>
    public static FormKey UnresolvableRaceKey => new(Mod, 0x902);

    public static FormKey NordicCarvedOutfitKey => new(Mod, 0x910);

    public static FormKey IronOutfitKey => new(Mod, 0x911);

    public static FormKey MultiOutfitAKey => new(Mod, 0x912);

    public static FormKey MultiOutfitBKey => new(Mod, 0x913);

    public static FormKey NcCuirassKey => new(Mod, 0x920);

    public static FormKey NcCuirassAddonKey => new(Mod, 0x921);

    public static FormKey NcHelmetKey => new(Mod, 0x922);

    public static FormKey NcHelmetAddonKey => new(Mod, 0x923);

    public static FormKey NcGauntletsKey => new(Mod, 0x924);

    public static FormKey NcGauntletsAddonKey => new(Mod, 0x925);

    public static FormKey NcBootsKey => new(Mod, 0x926);

    public static FormKey NcBootsAddonKey => new(Mod, 0x927);

    public static FormKey IronCuirassKey => new(Mod, 0x930);

    public static FormKey IronCuirassAddonKey => new(Mod, 0x931);

    public static FormKey IronGauntletsKey => new(Mod, 0x932);

    public static FormKey IronGauntletsAddonKey => new(Mod, 0x933);

    public static FormKey IronBootsKey => new(Mod, 0x934);

    public static FormKey IronBootsAddonKey => new(Mod, 0x935);

    public static FormKey LeatherCuirassKey => new(Mod, 0x940);

    public static FormKey LeatherCuirassAddonKey => new(Mod, 0x941);

    public static FormKey LeatherGauntletsKey => new(Mod, 0x942);

    public static FormKey LeatherGauntletsAddonKey => new(Mod, 0x943);

    public static FormKey BoarKey => new(Mod, 0x950);

    public static FormKey BoarAddonKey => new(Mod, 0x951);

    public static FormKey VampireRobesKey => new(Mod, 0x960);

    public static FormKey VampireRobesAddonKey => new(Mod, 0x961);

    public static FormKey MultiBootsKey => new(Mod, 0x970);

    public static FormKey MultiBootsAddonKey => new(Mod, 0x971);

    public static FormKey NoArmatureKey => new(Mod, 0x980);

    public static FormKey NoArmatureAddonDanglingKey => new(Mod, 0x981);

    public static FormKey EmptyModelKey => new(Mod, 0x982);

    public static FormKey EmptyModelAddonKey => new(Mod, 0x983);

    public static FormKey NoSlotKey => new(Mod, 0x984);

    public static FormKey NoSlotAddonKey => new(Mod, 0x985);

    public static FormKey NakedBodyKey => new(Mod, 0x986);

    public static FormKey NakedBodyAddonKey => new(Mod, 0x987);

    public static FormKey MysteryArmorKey => new(Mod, 0x988);

    public static FormKey MysteryArmorAddonKey => new(Mod, 0x989);

    public static FormKey HeavyKeywordKey => new(Mod, 0x990);

    public static FormKey LightKeywordKey => new(Mod, 0x991);

    public static FormKey ClothingKeywordKey => new(Mod, 0x992);

    public static string Write(string directory)
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);

        mod.Keywords.Add(new Keyword(HeavyKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorHeavy" });
        mod.Keywords.Add(new Keyword(LightKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorLight" });
        mod.Keywords.Add(new Keyword(ClothingKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorClothing" });

        mod.Races.Add(new Race(BoarRaceKey, SkyrimRelease.SkyrimSE) { EditorID = "BoarRace" });
        mod.Races.Add(new Race(NordVampireRaceKey, SkyrimRelease.SkyrimSE) { EditorID = "NordVampire" });

        AddArmor(mod, NcCuirassKey, NcCuirassAddonKey, "DLC2NordicCarvedCuirass", BipedObjectFlag.Body, race: null);
        AddArmor(mod, NcHelmetKey, NcHelmetAddonKey, "DLC2NordicCarvedHelmet", BipedObjectFlag.Head, race: null);
        AddArmor(mod, NcGauntletsKey, NcGauntletsAddonKey, "DLC2NordicCarvedGauntlets", BipedObjectFlag.Hands, race: null);
        AddArmor(mod, NcBootsKey, NcBootsAddonKey, "DLC2NordicCarvedBoots", BipedObjectFlag.Feet, race: null);

        AddArmor(mod, IronCuirassKey, IronCuirassAddonKey, "0A2C8841", BipedObjectFlag.Body, race: null);
        AddArmor(mod, IronGauntletsKey, IronGauntletsAddonKey, "0A2C8842", BipedObjectFlag.Hands, race: null);
        AddArmor(mod, IronBootsKey, IronBootsAddonKey, "0A2C8843", BipedObjectFlag.Feet, race: null);

        AddArmor(mod, LeatherCuirassKey, LeatherCuirassAddonKey, "ArmorLeatherCuirass", BipedObjectFlag.Body, race: null);
        AddArmor(mod, LeatherGauntletsKey, LeatherGauntletsAddonKey, "ArmorLeatherGauntlets", BipedObjectFlag.Hands, race: null);

        AddArmor(mod, BoarKey, BoarAddonKey, "Boar", BipedObjectFlag.Body, race: BoarRaceKey);
        AddArmor(mod, VampireRobesKey, VampireRobesAddonKey, "ClothesVampireRobes", BipedObjectFlag.Body, race: NordVampireRaceKey);

        AddArmor(mod, MultiBootsKey, MultiBootsAddonKey, "SharedBoots", BipedObjectFlag.Feet, race: null);

        AddArmor(mod, EmptyModelKey, EmptyModelAddonKey, "EmptyModelBoots", BipedObjectFlag.Feet, race: null, setModel: false);
        AddArmor(mod, NoSlotKey, NoSlotAddonKey, "NoSlotRing", BipedObjectFlag.Head, race: null, setFlags: false);
        AddArmor(mod, MysteryArmorKey, MysteryArmorAddonKey, "MysteryGauntlets", BipedObjectFlag.Hands, race: UnresolvableRaceKey);

        AddNoArmatureArmor(mod, NoArmatureKey, NoArmatureAddonDanglingKey, "DanglingOnly");
        AddNakedBody(mod, NakedBodyKey, NakedBodyAddonKey, "NakedBody");

        AddOutfit(mod, NordicCarvedOutfitKey, "DLC2NordicCarved", new[] { NcCuirassKey, NcHelmetKey });
        AddOutfit(mod, IronOutfitKey, "IronArmor", new[] { IronCuirassKey, IronGauntletsKey, IronBootsKey });
        AddOutfit(mod, MultiOutfitAKey, "aaSharedSet", new[] { MultiBootsKey });
        AddOutfit(mod, MultiOutfitBKey, "zzSharedSet", new[] { MultiBootsKey });

        var path = Path.Combine(directory, FileName);
        mod.WriteToBinary(path);
        return path;
    }

    private static void AddArmor(
        SkyrimMod mod,
        FormKey armorKey,
        FormKey addonKey,
        string editorId,
        BipedObjectFlag flags,
        FormKey? race,
        bool setModel = true,
        bool setFlags = true)
    {
        var bodyTemplate = setFlags && flags != (BipedObjectFlag)0
            ? new BodyTemplate { FirstPersonFlags = flags, ArmorType = ArmorType.HeavyArmor }
            : null;

        var addon = new ArmorAddon(addonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId + (race is null ? "AA" : "AA_" + race.Value.IDString()),
            BodyTemplate = bodyTemplate,
            WorldModel = setModel
                ? new GenderedItem<Model?>(MakeModel(editorId + ".nif"), MakeModel(editorId + ".nif"))
                : new GenderedItem<Model?>(null, null),
        };

        if (race is not null)
        {
            addon.Race = new FormLinkNullable<IRaceGetter>(race.Value);
        }

        mod.ArmorAddons.Add(addon);

        var armor = new Armor(armorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId,
            Name = editorId,
            BodyTemplate = bodyTemplate,
            Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>> { new FormLink<IKeywordGetter>(HeavyKeywordKey) },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(addonKey) },
        };
        mod.Armors.Add(armor);
    }

    private static void AddNoArmatureArmor(SkyrimMod mod, FormKey armorKey, FormKey danglingAddonKey, string editorId)
    {
        mod.Armors.Add(new Armor(armorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId,
            Name = editorId,
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Feet, ArmorType = ArmorType.HeavyArmor },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(danglingAddonKey) },
        });
    }

    private static void AddNakedBody(SkyrimMod mod, FormKey armorKey, FormKey addonKey, string editorId)
    {
        var addon = new ArmorAddon(addonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId + "AA",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            WorldModel = new GenderedItem<Model?>(MakeModel(editorId + ".nif"), MakeModel(editorId + ".nif")),
        };
        mod.ArmorAddons.Add(addon);

        var armor = new Armor(armorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId,
            Name = editorId,
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(addonKey) },
        };
        mod.Armors.Add(armor);
    }

    private static void AddOutfit(SkyrimMod mod, FormKey key, string editorId, IReadOnlyList<FormKey> members)
    {
        var items = new ExtendedList<IFormLinkGetter<IOutfitTargetGetter>>();
        foreach (var member in members)
        {
            items.Add(new FormLink<IOutfitTargetGetter>(member));
        }

        mod.Outfits.Add(new Outfit(key, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId,
            Items = items,
        });
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
