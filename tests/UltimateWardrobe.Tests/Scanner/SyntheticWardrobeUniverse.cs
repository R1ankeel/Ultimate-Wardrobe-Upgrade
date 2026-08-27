using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using Noggog;

namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// A minimal synthetic plugin exercising the Sprint 1.7.3 wardrobe filter: a cross-family
/// "MercenaryMixer" Outfit mixing Steel and Iron pieces must NOT tie unrelated families into one
/// set (vanilla NPC wardrobes behave this way), while single-family Outfits (IronArmor,
/// SteelTroupe) and the Steel EDID family keep members together.
/// </summary>
internal static class SyntheticWardrobeUniverse
{
    public const string FileName = "WardrobeUniverse.esp";

    public static ModKey Mod => ModKey.FromName(Path.GetFileNameWithoutExtension(FileName), ModType.Plugin);

    public static FormKey HeavyKeywordKey => new(Mod, 0xA00);

    public static FormKey SteelCuirassKey => new(Mod, 0xA10);
    public static FormKey SteelCuirassAddonKey => new(Mod, 0xA11);
    public static FormKey SteelBootsKey => new(Mod, 0xA12);
    public static FormKey SteelBootsAddonKey => new(Mod, 0xA13);
    public static FormKey IronCuirassKey => new(Mod, 0xA14);
    public static FormKey IronCuirassAddonKey => new(Mod, 0xA15);
    public static FormKey MixerOutfitKey => new(Mod, 0xA20);
    public static FormKey IronArmorOutfitKey => new(Mod, 0xA21);
    public static FormKey SteelTroupeOutfitKey => new(Mod, 0xA22);

    public static string Write(string directory)
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);
        mod.Keywords.Add(new Keyword(HeavyKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorHeavy" });

        AddArmor(mod, SteelCuirassKey, SteelCuirassAddonKey, "ArmorSteelCuirassA", BipedObjectFlag.Body);
        AddArmor(mod, SteelBootsKey, SteelBootsAddonKey, "ArmorSteelBootsA", BipedObjectFlag.Feet);
        AddArmor(mod, IronCuirassKey, IronCuirassAddonKey, "ArmorIronCuirass", BipedObjectFlag.Body);

        AddOutfit(mod, MixerOutfitKey, "MercenaryMixer", new[] { SteelCuirassKey, SteelBootsKey, IronCuirassKey });
        AddOutfit(mod, IronArmorOutfitKey, "IronArmor", new[] { IronCuirassKey });
        AddOutfit(mod, SteelTroupeOutfitKey, "SteelTroupe", new[] { SteelCuirassKey, SteelBootsKey });

        var path = Path.Combine(directory, FileName);
        mod.WriteToBinary(path);
        return path;
    }

    private static void AddArmor(SkyrimMod mod, FormKey armorKey, FormKey addonKey, string editorId, BipedObjectFlag flags)
    {
        var addon = new ArmorAddon(addonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId + "AA",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = flags, ArmorType = ArmorType.HeavyArmor },
            WorldModel = new GenderedItem<Model?>(MakeModel(editorId + ".nif"), MakeModel(editorId + ".nif")),
        };
        mod.ArmorAddons.Add(addon);

        mod.Armors.Add(new Armor(armorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId,
            Name = editorId,
            BodyTemplate = new BodyTemplate { FirstPersonFlags = flags, ArmorType = ArmorType.HeavyArmor },
            Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>> { new FormLink<IKeywordGetter>(HeavyKeywordKey) },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(addonKey) },
        });
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
        file.TrySetPath("meshes/armor/wardrobe/" + path);
        model.File = file;
        return model;
    }
}