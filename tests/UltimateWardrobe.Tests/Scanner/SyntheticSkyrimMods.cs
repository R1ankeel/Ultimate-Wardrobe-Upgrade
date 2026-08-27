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

        main.Armors.Add(new Armor(MeshArmorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "IronCuirass",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
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
}