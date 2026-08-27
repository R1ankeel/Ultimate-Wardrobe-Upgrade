using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.Core;

internal static class Fixtures
{
    public static Project CreateProject(string name = "TestProject", string root = "C:/Projects/Test")
    {
        return new Project(Guid.NewGuid(), name, root);
    }

    public static VanillaCatalogSource CreateVanillaSource(string root = "D:/Skymod/Stock Game")
    {
        return new VanillaCatalogSource(root, new[] { "Skyrim.esm", "Update.esm" });
    }

    public static StoryModCatalogSource CreateStorySource(string root = "C:/Mods/Vigilant", string main = "Vigilant.esp")
    {
        return new StoryModCatalogSource(root, main, new[] { "Skyrim.esm" });
    }

    public static Overhaul CreateOverhaul(Project project, string name = "VanillaOverhaul", CatalogSource? source = null)
    {
        source ??= CreateVanillaSource();
        return new Overhaul(Guid.NewGuid(), name, project.Id, source);
    }

    public static ArmorSet CreateArmorSet(string id = "IronArmor", string displayName = "Iron Armor")
    {
        var piece = new Piece("ArmorIronCuirass", 0x00012E46, "Body", "AA_IronCuirass", "armor/iron/cuirass.nif");
        var variant = new Variant(Gender.Male, WeightClass.Heavy, new[] { piece });
        return new ArmorSet(id, displayName, new[] { variant });
    }

    public static DonorAsset CreateDonorAsset(Guid? projectId = null, DonorAssetKind kind = DonorAssetKind.FullReplacer)
    {
        var pid = projectId ?? Guid.NewGuid();
        return new DonorAsset(
            Guid.NewGuid(),
            "test.7z",
            $"C:/Project/Source/{Guid.NewGuid()}",
            DateTime.UtcNow,
            "abc123hash",
            kind);
    }

    public static PieceMapping CreateMapping(Overhaul overhaul, DonorAsset donor, string setId = "IronArmor", string pieceEditor = "ArmorIronCuirass")
    {
        return new PieceMapping(
            Guid.NewGuid(),
            overhaul.Id,
            setId,
            pieceEditor,
            Gender.Male,
            donor.ImportId,
            "DonorIronCuirass",
            "armor/iron/cuirass.nif");
    }

    public static Catalog CreateCatalog(CatalogSource? source = null)
    {
        source ??= CreateVanillaSource();
        return new Catalog(source, new[] { CreateArmorSet() });
    }
}
