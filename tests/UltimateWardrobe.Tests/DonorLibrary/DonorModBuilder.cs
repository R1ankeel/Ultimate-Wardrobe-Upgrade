using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using Noggog;

namespace UltimateWardrobe.Tests.DonorLibrary;

/// <summary>
/// Sprint 2.1 donor fixtures built with the Mutagen writer. Follows the
/// <c>SyntheticSkyrimMods</c> story-mod mock pattern: small self-contained plugins whose
/// keyword, texture-set and armature records live either in the esp itself or in a bundled
/// helper master, so tests never depend on an installed game.
/// </summary>
internal static class DonorModBuilder
{
    // ---------------------------------------------------------------------------------
    // Self-contained donor: keyword + txst + arma + armo in ONE esp, no master references.
    // Expected classification: one ProvidedSet with Id "donorkit", DisplayName "Donor Kit",
    // Male+Female variants, weight Heavy, single piece with KitMeshPath.
    // ---------------------------------------------------------------------------------
    public const string SelfContainedFileName = "DonorKit.esp";

    public static ModKey SelfContainedKey => ModKey.FromFileName(SelfContainedFileName);

    public static FormKey KitKeywordKey => new(SelfContainedKey, 0x800);
    public static FormKey KitTextureSetKey => new(SelfContainedKey, 0x801);
    public static FormKey KitAddonKey => new(SelfContainedKey, 0x802);
    public static FormKey KitArmorKey => new(SelfContainedKey, 0x803);

    public const string KitMeshPath = "meshes/armor/donorkit/cuirass.nif";
    public const string KitDiffusePath = "textures/armor/donorkit/cuirass.dds";

    public static string WriteSelfContained(string directory)
    {
        Directory.CreateDirectory(directory);
        var mod = new SkyrimMod(SelfContainedKey, SkyrimRelease.SkyrimSE);

        mod.Keywords.Add(new Keyword(KitKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorHeavy" });

        var textureSet = new TextureSet(KitTextureSetKey, SkyrimRelease.SkyrimSE) { EditorID = "DonorKitTxst" };
        textureSet.Diffuse = MakeTexturePath(KitDiffusePath);
        mod.TextureSets.Add(textureSet);

        mod.ArmorAddons.Add(new ArmorAddon(KitAddonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "DonorKitCuirassAA",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            WorldModel = new GenderedItem<Model?>(
                MakeModel(KitMeshPath),
                MakeModel(KitMeshPath)),
            SkinTexture = new GenderedItem<IFormLinkNullableGetter<ITextureSetGetter>>(
                new FormLinkNullable<ITextureSetGetter>(KitTextureSetKey),
                new FormLinkNullable<ITextureSetGetter>(KitTextureSetKey)),
        });

        mod.Armors.Add(new Armor(KitArmorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "DonorKitCuirass",
            Name = "Donor Kit Cuirass",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>> { new FormLink<IKeywordGetter>(KitKeywordKey) },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(KitAddonKey) },
        });

        var path = Path.Combine(directory, SelfContainedFileName);
        mod.WriteToBinary(path);
        return path;
    }

    // ---------------------------------------------------------------------------------
    // Reference base: an esm carrying a resolvable ArmorHeavy keyword (RefKeywordKey) and an
    // unrelated reference ARMO ("RefMageRobes") that must never leak into ProvidedSets because
    // its FormKey.ModKey belongs to the reference, not the donor.
    // Expected: contributes the keyword resolution; RefMageRobes never output.
    // ---------------------------------------------------------------------------------
    public const string RefBaseFileName = "RefBase.esm";

    public static ModKey RefBaseKey => ModKey.FromFileName(RefBaseFileName);

    public static FormKey RefKeywordKey => new(RefBaseKey, 0x900);
    public static FormKey RefMageRobesAddonKey => new(RefBaseKey, 0x910);
    public static FormKey RefMageRobesKey => new(RefBaseKey, 0x911);

    public static string WriteReferenceBase(string directory)
    {
        Directory.CreateDirectory(directory);
        var mod = new SkyrimMod(RefBaseKey, SkyrimRelease.SkyrimSE);

        mod.Keywords.Add(new Keyword(RefKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorHeavy" });

        mod.ArmorAddons.Add(new ArmorAddon(RefMageRobesAddonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "RefMageRobesAA",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.Clothing },
            WorldModel = new GenderedItem<Model?>(
                MakeModel("meshes/clothes/magerobes/robes.nif"),
                MakeModel("meshes/clothes/magerobes/robes.nif")),
        });

        mod.Armors.Add(new Armor(RefMageRobesKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "RefMageRobes",
            Name = "Ref Mage Robes",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.Clothing },
            Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>> { new FormLink<IKeywordGetter>(RefKeywordKey) },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(RefMageRobesAddonKey) },
        });

        var path = Path.Combine(directory, RefBaseFileName);
        mod.WriteToBinary(path);
        return path;
    }

    // ---------------------------------------------------------------------------------
    // Donor esp that depends on the reference ArmorHeavy keyword (RefKeywordKey): its own armature
    // and armor live in the esp, the keyword link points into RefBase. Without a reference root
    // the keyword cannot resolve -> classified NoKeyword -> zero sets. With it -> one set
    // "donorrp" ("DonorRpCuirass" strips "Cuirass").
    // ---------------------------------------------------------------------------------
    public const string ReferenceDependentFileName = "DonorRef.esp";

    public static ModKey ReferenceDependentKey => ModKey.FromFileName(ReferenceDependentFileName);

    public static FormKey RefDepAddonKey => new(ReferenceDependentKey, 0x820);
    public static FormKey RefDepArmorKey => new(ReferenceDependentKey, 0x821);

    public static string WriteReferenceDependent(string directory)
    {
        Directory.CreateDirectory(directory);
        var mod = new SkyrimMod(ReferenceDependentKey, SkyrimRelease.SkyrimSE);
        ((IMod)mod).MasterReferences.Add(new MasterReference { Master = RefBaseKey });

        mod.ArmorAddons.Add(new ArmorAddon(RefDepAddonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "DonorRpCuirassAA",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            WorldModel = new GenderedItem<Model?>(
                MakeModel("meshes/armor/donorrp/cuirass.nif"),
                MakeModel("meshes/armor/donorrp/cuirass.nif")),
        });

        mod.Armors.Add(new Armor(RefDepArmorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "DonorRpCuirass",
            Name = "Donor Rp Cuirass",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>> { new FormLink<IKeywordGetter>(RefKeywordKey) },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(RefDepAddonKey) },
        });

        var path = Path.Combine(directory, ReferenceDependentFileName);
        mod.WriteToBinary(path);
        return path;
    }

    // ---------------------------------------------------------------------------------
    // Bundled master: the ArmorHeavy keyword record lives in a helper esm INSIDE the donor
    // folder, and the main esp references it. Tests master resolution entirely from donor
    // files (probe picks the esp as the unreferenced main; both files are donor keys).
    // Expected: one set "bundledkit".
    // ---------------------------------------------------------------------------------
    public const string BundledMasterFileName = "BundledBase.esm";
    public const string BundledKitFileName = "BundledKit.esp";

    public static ModKey BundledMasterKey => ModKey.FromFileName(BundledMasterFileName);
    public static ModKey BundledKitKey => ModKey.FromFileName(BundledKitFileName);

    public static FormKey BundledKeywordKey => new(BundledMasterKey, 0x840);
    public static FormKey BundledAddonKey => new(BundledKitKey, 0x841);
    public static FormKey BundledArmorKey => new(BundledKitKey, 0x842);

    public static string WriteBundledMaster(string directory)
    {
        Directory.CreateDirectory(directory);
        var master = new SkyrimMod(BundledMasterKey, SkyrimRelease.SkyrimSE);
        master.Keywords.Add(new Keyword(BundledKeywordKey, SkyrimRelease.SkyrimSE) { EditorID = "ArmorHeavy" });
        master.WriteToBinary(Path.Combine(directory, BundledMasterFileName));

        var mod = new SkyrimMod(BundledKitKey, SkyrimRelease.SkyrimSE);
        ((IMod)mod).MasterReferences.Add(new MasterReference { Master = BundledMasterKey });

        mod.ArmorAddons.Add(new ArmorAddon(BundledAddonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "BundledKitCuirassAA",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            WorldModel = new GenderedItem<Model?>(
                MakeModel("meshes/armor/bundledkit/cuirass.nif"),
                MakeModel("meshes/armor/bundledkit/cuirass.nif")),
        });

        mod.Armors.Add(new Armor(BundledArmorKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "BundledKitCuirass",
            Name = "Bundled Kit Cuirass",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>> { new FormLink<IKeywordGetter>(BundledKeywordKey) },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(BundledAddonKey) },
        });

        var path = Path.Combine(directory, BundledKitFileName);
        mod.WriteToBinary(path);
        return path;
    }

    // ---------------------------------------------------------------------------------
    // A loadable esp with NO ARMO records (a keyword + txst only). Used for the 0-ARMO
    // fall-through path.
    // ---------------------------------------------------------------------------------
    public const string EmptyEspFileName = "EmptyKit.esp";

    public static ModKey EmptyEspKey => ModKey.FromFileName(EmptyEspFileName);

    public static string WriteEmptyEsp(string directory, string fileName = EmptyEspFileName)
    {
        Directory.CreateDirectory(directory);
        var key = ModKey.FromFileName(fileName);
        var mod = new SkyrimMod(key, SkyrimRelease.SkyrimSE);
        mod.Keywords.Add(new Keyword(new FormKey(key, 0x860), SkyrimRelease.SkyrimSE) { EditorID = "ArmorHeavy" });
        var path = Path.Combine(directory, fileName);
        mod.WriteToBinary(path);
        return path;
    }

    // ---------------------------------------------------------------------------------
    // Merger fixtures: a fake game root whose Data/ folder holds esm/esl masters.
    // ---------------------------------------------------------------------------------
    public const string RefGameRootFileName = "RefGameA.esm";
    public const string RefGameEslFileName = "RefGameB.esl";

    public static ModKey RefGameRootKey => ModKey.FromFileName(RefGameRootFileName);
    public static ModKey RefGameEslKey => ModKey.FromFileName(RefGameEslFileName);

    public static string WriteEmptyReference(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var key = ModKey.FromFileName(fileName);
        var mod = new SkyrimMod(key, SkyrimRelease.SkyrimSE);
        var path = Path.Combine(directory, fileName);
        mod.WriteToBinary(path);
        return path;
    }

    /// <summary>
    /// Writes an esm exposing the weight keyword at (key, 0x900) with an arbitrary EditorID.
    /// Used by the reference-merge ordering test to prove the donor-bundled copy wins.
    /// </summary>
    public static string WriteReferenceKeyword(string directory, string fileName, string keywordEditorId)
    {
        Directory.CreateDirectory(directory);
        var key = ModKey.FromFileName(fileName);
        var mod = new SkyrimMod(key, SkyrimRelease.SkyrimSE);
        mod.Keywords.Add(new Keyword(new FormKey(key, 0x900), SkyrimRelease.SkyrimSE) { EditorID = keywordEditorId });
        var path = Path.Combine(directory, fileName);
        mod.WriteToBinary(path);
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
}