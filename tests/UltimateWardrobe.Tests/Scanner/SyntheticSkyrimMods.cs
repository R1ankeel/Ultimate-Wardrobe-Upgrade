using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using Noggog;

namespace UltimateWardrobe.Tests.Scanner;

internal sealed class TestTempDir : IDisposable
{
    public string Root { get; }

    public TestTempDir()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UW_Sprint11_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string File(string name) => System.IO.Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class SyntheticSkyrimMods
{
    public const string MasterFileName = "FakeMaster.esp";
    public const string MainFileName = "FakeMain.esp";

    public static ModKey MasterKey => ModKey.FromName(Path.GetFileNameWithoutExtension(MasterFileName), ModType.Plugin);

    public static ModKey MainKey => ModKey.FromName(Path.GetFileNameWithoutExtension(MainFileName), ModType.Plugin);

    public static FormKey OverrideArmorKey => new(MasterKey, 0x800);

    public static FormKey NewArmorKey => new(MainKey, 0x840);

    public static FormKey WeightKeywordKey => new(MasterKey, 0x810);

    public static FormKey NonWeightKeywordKey => new(MasterKey, 0x811);

    public static FormKey MasterTextureSetKey => new(MasterKey, 0x820);

    public static FormKey MainArmorAddonKey => new(MainKey, 0x830);

    public static FormKey MainHeavyKeywordKey => new(MainKey, 0x841);

    public static FormKey MeshTextureSetKey => new(MasterKey, 0x870);

    public static FormKey MeshArmorKey => new(MainKey, 0x850);

    public static FormKey MeshAddonKey => new(MainKey, 0x860);

    public static FormKey DanglingArmorKey => new(MainKey, 0x880);

    public static FormKey DanglingAddonKey => new(MainKey, 0x881);

    public static string MeshPath => "meshes/armor/iron/cuirass_1.nif";

    public static string MeshDiffusePath => "textures/armor/iron/cuirass_1.dds";

    public static string MeshNormalPath => "textures/armor/iron/cuirass_1_n.dds";

    public static string MeshGlowPath => "textures/armor/iron/gold.dds";

    public static string WriteMaster(string directory)
    {
        var master = new SkyrimMod(MasterKey, SkyrimRelease.SkyrimSE);
        master.Armors.Add(new Armor(OverrideArmorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "BaseArmor",
            Name = "Base Armor",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
        });
        master.Keywords.Add(new Keyword(WeightKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorHeavy" });
        master.Keywords.Add(new Keyword(NonWeightKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "VendorItemClothing" });
        master.TextureSets.Add(new TextureSet(MasterTextureSetKey, SkyrimRelease.SkyrimSE) { EditorID = "txSetMaster" });

        var meshTextureSet = new TextureSet(MeshTextureSetKey, SkyrimRelease.SkyrimSE) { EditorID = "txSetIron" };
        meshTextureSet.Diffuse = MakeTexturePath(MeshDiffusePath);
        meshTextureSet.NormalOrGloss = MakeTexturePath(MeshNormalPath);
        meshTextureSet.GlowOrDetailMap = MakeTexturePath(MeshGlowPath);
        master.TextureSets.Add(meshTextureSet);

        var path = Path.Combine(directory, MasterFileName);
        master.WriteToBinary(path);
        return path;
    }

    public static string WriteMain(string directory)
    {
        var main = new SkyrimMod(MainKey, SkyrimRelease.SkyrimSE);
        ((IMod)main).MasterReferences.Add(new MasterReference { Master = MasterKey });
        main.Armors.Add(new Armor(OverrideArmorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "BaseArmor",
            Name = "Patched Armor",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
        });
        main.Armors.Add(new Armor(NewArmorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "NewHelm",
            Name = "New Helm",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Head, ArmorType = ArmorType.LightArmor },
        });
        main.ArmorAddons.Add(new ArmorAddon(MainArmorAddonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "BaseArmorAA",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            SkinTexture = new GenderedItem<IFormLinkNullableGetter<ITextureSetGetter>>(
                new FormLinkNullable<ITextureSetGetter>(MasterTextureSetKey),
                new FormLinkNullable<ITextureSetGetter>(MasterTextureSetKey)),
        });

        var meshAddon = new ArmorAddon(MeshAddonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "IronCuirassAA",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            WorldModel = new GenderedItem<Model?>(
                MakeModel(MeshPath),
                MakeModel(MeshPath)),
            SkinTexture = new GenderedItem<IFormLinkNullableGetter<ITextureSetGetter>>(
                new FormLinkNullable<ITextureSetGetter>(MeshTextureSetKey),
                new FormLinkNullable<ITextureSetGetter>(MeshTextureSetKey)),
        };
        main.ArmorAddons.Add(meshAddon);

        // The keyword record lives in FakeMain itself so it stays resolvable even when the master
        // is absent (the missing-master scan path); the Iron kit then groups instead of skipping.
        main.Keywords.Add(new Keyword(MainHeavyKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorHeavy" });
        main.Armors.Add(new Armor(MeshArmorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "IronCuirass",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>> { new FormLink<IKeywordGetter>(MainHeavyKeywordKey) },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(MeshAddonKey) },
        });

        main.Armors.Add(new Armor(DanglingArmorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "DanglingGauntlets",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Hands, ArmorType = ArmorType.HeavyArmor },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(DanglingAddonKey) },
        });

        var path = Path.Combine(directory, MainFileName);
        main.WriteToBinary(path);
        return path;
    }

    private static Model MakeModel(string path)
    {
        var model = new Model();
        var file = new AssetLink<SkyrimModelAssetType>();
        file.TrySetPath(path);
        model.File = file;
        return model;
    }

    private static AssetLink<SkyrimTextureAssetType> MakeTexturePath(string path)
    {
        var link = new AssetLink<SkyrimTextureAssetType>();
        link.TrySetPath(path);
        return link;
    }

    public static string WriteChainPlugin(string directory, string fileName, params string[] masterFileNames)
    {
        var key = ModKey.FromName(Path.GetFileNameWithoutExtension(fileName), ModType.Plugin);
        var mod = new SkyrimMod(key, SkyrimRelease.SkyrimSE);
        foreach (var masterName in masterFileNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var masterKey = ModKey.FromName(Path.GetFileNameWithoutExtension(masterName), ModType.Plugin);
            mod.Keywords.Add(new Keyword(new FormKey(masterKey, 0x801), SkyrimRelease.SkyrimSE) { EditorID = "ChainMasterRef" });
        }

        var path = Path.Combine(directory, fileName);
        mod.WriteToBinary(path);
        return path;
    }

    public static string WriteCorruptPlugin(string directory, string fileName = "Broken.esp")
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, new byte[] { 0x52, 0x4E, 0x59, 0x53, 0x4B, 0x52, 0x49, 0x4D, 0xFF, 0x00, 0x42, 0xAD, 0x21 });
        return path;
    }

    // ---------------------------------------------------------------------------------------------
    // Sprint 1.6 mini universe (the golden fixture).
    //
    // One self-contained plugin (no external masters) exercising the full feature spread:
    //   - Iron: a full four-piece set driven by one Outfit (the happy path).
    //   - Nordic Carved: split membership - cuirass+helmet in an Outfit, gauntlets+boots outside,
    //     EDIDs crafted so KeyNormalizer yields the same key as the Outfit EditorID.
    //   - Leather: a pure EDID/mesh-fallback full set (no Outfit at all).
    //   - Vampire robes: an Outfit-driven clothing set tied to a playable vampire race.
    //   - Gender-specific covers: female-only and male-only armors (model signals).
    //   - Weird EDIDs: sort-last "zzz" prefix, Creation-Club "cc...-ba_" prefix, dirty "!!" leading
    //     symbols.
    //   - Jewelry: an Amulet ARMO (skipped as jewelry since Sprint 6.8) and a Circlet ARMO (kept - a
    //     head slot, not jewelry).
    //   - Dummy ARMO without any armature (-> NoArmature skip).
    //   - Boar-style creature-skin ARMO tied to a non-playable race (-> CreatureRace skip).
    //   - A pauldron belonging to two Outfits (deterministic tie-break).
    // ---------------------------------------------------------------------------------------------
    public const string MiniUniverseFileName = "MiniUniverse.esp";

    public static ModKey MiniUniverseKey => ModKey.FromName(Path.GetFileNameWithoutExtension(MiniUniverseFileName), ModType.Plugin);

    public static FormKey MiniHeavyKeywordKey => new(MiniUniverseKey, 0xE00);
    public static FormKey MiniLightKeywordKey => new(MiniUniverseKey, 0xE01);
    public static FormKey MiniClothingKeywordKey => new(MiniUniverseKey, 0xE02);
    public static FormKey MiniBoarRaceKey => new(MiniUniverseKey, 0xE10);
    public static FormKey MiniVampireRaceKey => new(MiniUniverseKey, 0xE11);
    public static FormKey MiniIronOutfitKey => new(MiniUniverseKey, 0xE20);
    public static FormKey MiniNordicOutfitKey => new(MiniUniverseKey, 0xE21);
    public static FormKey MiniVampireRobesOutfitKey => new(MiniUniverseKey, 0xE22);
    public static FormKey MiniPauldronSetAKey => new(MiniUniverseKey, 0xE23);
    public static FormKey MiniPauldronSetBKey => new(MiniUniverseKey, 0xE24);

    public static FormKey MiniIronCuirassKey => new(MiniUniverseKey, 0xE30);
    public static FormKey MiniIronHelmetKey => new(MiniUniverseKey, 0xE31);
    public static FormKey MiniIronGauntletsKey => new(MiniUniverseKey, 0xE32);
    public static FormKey MiniIronBootsKey => new(MiniUniverseKey, 0xE33);
    public static FormKey MiniIronCuirassAddonKey => new(MiniUniverseKey, 0xE34);

    public static string MiniIronCuirassMalePath => "meshes/armor/mini/IronCuirass/male.nif";

    public static string MiniIronCuirassFemalePath => "meshes/armor/mini/IronCuirass/female.nif";
    public static FormKey MiniIronHelmetAddonKey => new(MiniUniverseKey, 0xE35);
    public static FormKey MiniIronGauntletsAddonKey => new(MiniUniverseKey, 0xE36);
    public static FormKey MiniIronBootsAddonKey => new(MiniUniverseKey, 0xE37);

    public static FormKey MiniNcCuirassKey => new(MiniUniverseKey, 0xE40);
    public static FormKey MiniNcHelmetKey => new(MiniUniverseKey, 0xE41);
    public static FormKey MiniNcGauntletsKey => new(MiniUniverseKey, 0xE42);
    public static FormKey MiniNcBootsKey => new(MiniUniverseKey, 0xE43);
    public static FormKey MiniNcCuirassAddonKey => new(MiniUniverseKey, 0xE44);
    public static FormKey MiniNcHelmetAddonKey => new(MiniUniverseKey, 0xE45);
    public static FormKey MiniNcGauntletsAddonKey => new(MiniUniverseKey, 0xE46);
    public static FormKey MiniNcBootsAddonKey => new(MiniUniverseKey, 0xE47);

    public static FormKey MiniLeatherCuirassKey => new(MiniUniverseKey, 0xE50);
    public static FormKey MiniLeatherHelmetKey => new(MiniUniverseKey, 0xE51);
    public static FormKey MiniLeatherGauntletsKey => new(MiniUniverseKey, 0xE52);
    public static FormKey MiniLeatherBootsKey => new(MiniUniverseKey, 0xE53);
    public static FormKey MiniLeatherCuirassAddonKey => new(MiniUniverseKey, 0xE54);
    public static FormKey MiniLeatherHelmetAddonKey => new(MiniUniverseKey, 0xE55);
    public static FormKey MiniLeatherGauntletsAddonKey => new(MiniUniverseKey, 0xE56);
    public static FormKey MiniLeatherBootsAddonKey => new(MiniUniverseKey, 0xE57);

    public static FormKey MiniVampireRobesKey => new(MiniUniverseKey, 0xE60);
    public static FormKey MiniVampireRobesAddonKey => new(MiniUniverseKey, 0xE61);
    public static FormKey MiniFemaleCorsetKey => new(MiniUniverseKey, 0xE62);
    public static FormKey MiniFemaleCorsetAddonKey => new(MiniUniverseKey, 0xE63);
    public static FormKey MiniMaleBulwarkKey => new(MiniUniverseKey, 0xE64);
    public static FormKey MiniMaleBulwarkAddonKey => new(MiniUniverseKey, 0xE65);
    public static FormKey MiniNightmareSuBracersKey => new(MiniUniverseKey, 0xE66);
    public static FormKey MiniNightmareSuBracersAddonKey => new(MiniUniverseKey, 0xE67);
    public static FormKey MiniDaedricCuirassKey => new(MiniUniverseKey, 0xE68);
    public static FormKey MiniDaedricCuirassAddonKey => new(MiniUniverseKey, 0xE69);
    public static FormKey MiniChampionKey => new(MiniUniverseKey, 0xE6A);
    public static FormKey MiniChampionAddonKey => new(MiniUniverseKey, 0xE6B);
    public static FormKey MiniElvenAmuletKey => new(MiniUniverseKey, 0xE6C);
    public static FormKey MiniElvenAmuletAddonKey => new(MiniUniverseKey, 0xE6D);
    public static FormKey MiniOrcishCircletKey => new(MiniUniverseKey, 0xE6E);
    public static FormKey MiniOrcishCircletAddonKey => new(MiniUniverseKey, 0xE6F);
    public static FormKey MiniDummyKey => new(MiniUniverseKey, 0xE70);
    public static FormKey MiniBoarHideKey => new(MiniUniverseKey, 0xE71);
    public static FormKey MiniBoarHideAddonKey => new(MiniUniverseKey, 0xE72);
    public static FormKey MiniPauldronKey => new(MiniUniverseKey, 0xE73);
    public static FormKey MiniPauldronAddonKey => new(MiniUniverseKey, 0xE74);

    private enum MiniGender { Both, Female, Male }

    public static string WriteMiniUniverse(string directory)
    {
        var mod = new SkyrimMod(MiniUniverseKey, SkyrimRelease.SkyrimSE);

        mod.Keywords.Add(new Keyword(MiniHeavyKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorHeavy" });
        mod.Keywords.Add(new Keyword(MiniLightKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorLight" });
        mod.Keywords.Add(new Keyword(MiniClothingKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorClothing" });
        mod.Races.Add(new Race(MiniBoarRaceKey, SkyrimRelease.SkyrimSE) { EditorID = "MiniBoarRace" });
        mod.Races.Add(new Race(MiniVampireRaceKey, SkyrimRelease.SkyrimSE) { EditorID = "NordRaceVampire" });

        AddMiniArmorSet(mod, "Iron", 0xE30, 0xE34, 8, BipedObjectFlag.Body | BipedObjectFlag.Head | BipedObjectFlag.Hands | BipedObjectFlag.Feet, ArmorType.HeavyArmor);
        AddMiniArmorSet(mod, "DLC2NordicCarved", 0xE40, 0xE44, 8, BipedObjectFlag.Body | BipedObjectFlag.Head | BipedObjectFlag.Hands | BipedObjectFlag.Feet, ArmorType.HeavyArmor);
        AddMiniArmorSet(mod, "ArmorLeather", 0xE50, 0xE54, 8, BipedObjectFlag.Body | BipedObjectFlag.Head | BipedObjectFlag.Hands | BipedObjectFlag.Feet, ArmorType.LightArmor);

        AddMiniArmor(mod, MiniVampireRobesKey, MiniVampireRobesAddonKey, "ClothesVampireRobes",
            BipedObjectFlag.Body, ArmorType.Clothing, raceKey: MiniVampireRaceKey, gender: MiniGender.Both);
        AddMiniArmor(mod, MiniFemaleCorsetKey, MiniFemaleCorsetAddonKey, "FemaleCorset",
            BipedObjectFlag.Body, ArmorType.Clothing, raceKey: null, gender: MiniGender.Female);
        AddMiniArmor(mod, MiniMaleBulwarkKey, MiniMaleBulwarkAddonKey, "MaleBulwark",
            BipedObjectFlag.Body, ArmorType.HeavyArmor, raceKey: null, gender: MiniGender.Male);
        AddMiniArmor(mod, MiniNightmareSuBracersKey, MiniNightmareSuBracersAddonKey, "zzzNightmareSuitBracers",
            BipedObjectFlag.Hands, ArmorType.HeavyArmor, raceKey: null, gender: MiniGender.Both);
        AddMiniArmor(mod, MiniDaedricCuirassKey, MiniDaedricCuirassAddonKey, "ccBGSSSE063-ba_daedricCuirass",
            BipedObjectFlag.Body, ArmorType.HeavyArmor, raceKey: null, gender: MiniGender.Both);
        AddMiniArmor(mod, MiniChampionKey, MiniChampionAddonKey, "!!ChampionArmor",
            BipedObjectFlag.Body, ArmorType.HeavyArmor, raceKey: null, gender: MiniGender.Both);
        AddMiniArmor(mod, MiniElvenAmuletKey, MiniElvenAmuletAddonKey, "ElvenAmulet",
            BipedObjectFlag.Amulet, ArmorType.Clothing, raceKey: null, gender: MiniGender.Both);
        AddMiniArmor(mod, MiniOrcishCircletKey, MiniOrcishCircletAddonKey, "OrcishCirclet",
            BipedObjectFlag.Circlet, ArmorType.Clothing, raceKey: null, gender: MiniGender.Both);
        AddMiniArmor(mod, MiniBoarHideKey, MiniBoarHideAddonKey, "MiniBoarHide",
            BipedObjectFlag.Body, ArmorType.HeavyArmor, raceKey: MiniBoarRaceKey, gender: MiniGender.Both);
        AddMiniArmor(mod, MiniPauldronKey, MiniPauldronAddonKey, "MiniPauldron",
            BipedObjectFlag.Shield, ArmorType.HeavyArmor, raceKey: null, gender: MiniGender.Both);

        AddMiniDummy(mod, MiniDummyKey, "DummyMannequin");

        AddMiniOutfit(mod, MiniIronOutfitKey, "IronArmor",
            new[] { MiniIronCuirassKey, MiniIronHelmetKey, MiniIronGauntletsKey, MiniIronBootsKey });
        AddMiniOutfit(mod, MiniNordicOutfitKey, "DLC2NordicCarved",
            new[] { MiniNcCuirassKey, MiniNcHelmetKey });
        AddMiniOutfit(mod, MiniVampireRobesOutfitKey, "ClothesVampireRobes", new[] { MiniVampireRobesKey });
        AddMiniOutfit(mod, MiniPauldronSetAKey, "aaMiniPauldron", new[] { MiniPauldronKey });
        AddMiniOutfit(mod, MiniPauldronSetBKey, "zzMiniPauldron", new[] { MiniPauldronKey });

        var path = Path.Combine(directory, MiniUniverseFileName);
        mod.WriteToBinary(path);
        return path;
    }

    private static void AddMiniArmorSet(
        SkyrimMod mod,
        string prefix,
        int armorFormId,
        int addonFormId,
        int count,
        BipedObjectFlag flags,
        ArmorType armorType)
    {
        var slots = new[] { BipedObjectFlag.Body, BipedObjectFlag.Head, BipedObjectFlag.Hands, BipedObjectFlag.Feet };
        var pieces = new[] { "Cuirass", "Helmet", "Gauntlets", "Boots" };

        for (var i = 0; i < count / 2; i++)
        {
            var editorId = prefix + pieces[i];
            AddMiniArmor(mod,
                new FormKey(MiniUniverseKey, (uint)(armorFormId + i)),
                new FormKey(MiniUniverseKey, (uint)(addonFormId + i)),
                editorId,
                slots[i],
                armorType,
                raceKey: null,
                gender: MiniGender.Both);
        }
    }

    private static void AddMiniArmor(
        SkyrimMod mod,
        FormKey armorKey,
        FormKey addonKey,
        string editorId,
        BipedObjectFlag slots,
        ArmorType armorType,
        FormKey? raceKey,
        MiniGender gender)
    {
        var keywordKey = armorType switch
        {
            ArmorType.HeavyArmor => MiniHeavyKeywordKey,
            ArmorType.LightArmor => MiniLightKeywordKey,
            _ => MiniClothingKeywordKey,
        };

        var bodyTemplate = new BodyTemplate { FirstPersonFlags = slots, ArmorType = armorType };

        var addon = new ArmorAddon(addonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId + "AA",
            BodyTemplate = bodyTemplate,
            WorldModel = MakeGenderedModel(editorId, gender),
        };
        if (raceKey is not null)
        {
            addon.Race = new FormLinkNullable<IRaceGetter>(raceKey.Value);
        }

        mod.ArmorAddons.Add(addon);

        var armor = new Armor(armorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId,
            Name = editorId,
            BodyTemplate = bodyTemplate,
            Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>> { new FormLink<IKeywordGetter>(keywordKey) },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(addonKey) },
        };
        mod.Armors.Add(armor);
    }

    private static void AddMiniDummy(SkyrimMod mod, FormKey armorKey, string editorId)
    {
        mod.Armors.Add(new Armor(armorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId,
            Name = editorId,
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>> { new FormLink<IKeywordGetter>(MiniHeavyKeywordKey) },
        });
    }

    private static void AddMiniOutfit(SkyrimMod mod, FormKey key, string editorId, IReadOnlyList<FormKey> members)
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

    private static GenderedItem<Model?> MakeGenderedModel(string editorId, MiniGender gender)
    {
        var path = (string side) => "meshes/armor/mini/" + editorId + "/" + side + ".nif";

        return gender switch
        {
            MiniGender.Female => new GenderedItem<Model?>(null, MakeModel(path("female"))),
            MiniGender.Male => new GenderedItem<Model?>(MakeModel(path("male")), null),
            _ => new GenderedItem<Model?>(MakeModel(path("male")), MakeModel(path("female"))),
        };
    }
}