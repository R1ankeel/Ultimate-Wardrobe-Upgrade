using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

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

        var path = Path.Combine(directory, MainFileName);
        main.WriteToBinary(path);
        return path;
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