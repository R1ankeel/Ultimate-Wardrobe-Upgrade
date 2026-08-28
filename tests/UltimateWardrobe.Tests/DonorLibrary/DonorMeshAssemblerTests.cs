using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class DonorMeshAssemblerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"UW_Donor_B2_{Guid.NewGuid():N}");

    public DonorMeshAssemblerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string DonorFolder()
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Iron_Kit_Classifies_Into_Expected_Family()
    {
        var dir = DonorFolder();
        DonorMeshTreeBuilder.Write(dir,
            "meshes/armor/iron/f/cuirass.nif",
            "meshes/armor/iron/f/gauntlets.nif",
            "meshes/armor/iron/f/boots.nif",
            "meshes/armor/iron/f/helmet.nif",
            "meshes/armor/iron/m/cuirass.nif",
            "meshes/armor/iron/m/gauntlets.nif",
            "meshes/armor/iron/m/boots.nif",
            "meshes/armor/iron/m/helmet.nif",
            "textures/armor/iron/cuirass.dds",
            "textures/armor/iron/gauntlets.dds",
            "textures/armor/iron/boots.dds",
            "textures/armor/iron/helmet.dds");

        var donor = await new DonorClassifier().ClassifyAsync(dir);

        donor.Kind.Should().Be(DonorAssetKind.Unknown);
        var set = donor.ProvidedSets.Should().ContainSingle().Subject;
        set.Id.Should().Be("iron");
        set.DisplayName.Should().Be("Iron");

        set.Variants.Should().HaveCount(2);
        set.Variants.Select(v => v.Gender).Should().Equal(Gender.Male, Gender.Female);
        set.Variants.Should().OnlyContain(v => v.Weight == WeightClass.Any);

        var male = set.Variants.Single(v => v.Gender == Gender.Male);
        male.Pieces.Should().HaveCount(4);
        male.Pieces.Select(p => p.EditorId).Should().Equal("boots", "cuirass", "gauntlets", "helmet");
        male.Pieces.Select(p => p.Slot).Should().Equal("Boots", "Cuirass", "Gauntlets", "Helmet");

        var maleCuirass = male.Pieces.Single(p => p.EditorId == "cuirass");
        maleCuirass.MeshPath.Should().Be("meshes/armor/iron/m/cuirass.nif");
        maleCuirass.FormId.Should().Be(0);
        maleCuirass.TexturePaths.Should().ContainSingle().Which.Should().Be("textures/armor/iron/cuirass.dds");

        var femaleBoots = set.Variants.Single(v => v.Gender == Gender.Female)
            .Pieces.Single(p => p.EditorId == "boots");
        femaleBoots.MeshPath.Should().Be("meshes/armor/iron/f/boots.nif");
        femaleBoots.TexturePaths.Should().ContainSingle().Which.Should().Be("textures/armor/iron/boots.dds");
    }

    [Fact]
    public async Task Clothes_Path_Yields_Clothing_Weight_From_Data_Layout()
    {
        var dir = DonorFolder();
        DonorMeshTreeBuilder.Write(dir,
            "Data/meshes/clothes/collegerobes/f/robes.nif",
            "Data/meshes/clothes/collegerobes/m/robes.nif");

        var donor = await new DonorClassifier().ClassifyAsync(dir);

        var set = donor.ProvidedSets.Should().ContainSingle().Subject;
        set.Id.Should().Be("collegerobes");
        set.DisplayName.Should().Be("Collegerobes");
        set.Variants.Select(v => v.Gender).Should().Equal(Gender.Male, Gender.Female);
        set.Variants.Should().OnlyContain(v => v.Weight == WeightClass.Clothing);
        foreach (var variant in set.Variants)
        {
            var piece = variant.Pieces.Should().ContainSingle().Subject;
            piece.EditorId.Should().Be("robes");
            piece.Slot.Should().Be("Robes");
        }

        donor.FileManifest.Should().Contain(e => e.RelativePath == "Data/meshes/clothes/collegerobes/f/robes.nif");
    }

    [Fact]
    public async Task Unhelpful_Folder_Names_Fall_Back_To_Mesh_Folder_Key()
    {
        var dir = DonorFolder();
        DonorMeshTreeBuilder.Write(dir, "meshes/zzzztexture/whatever.nif");

        var donor = await new DonorClassifier().ClassifyAsync(dir);

        var set = donor.ProvidedSets.Should().ContainSingle().Subject;
        set.Id.Should().Be("zzzztexture");
        set.DisplayName.Should().Be("Zzzztexture");

        var variant = set.Variants.Should().ContainSingle().Subject;
        variant.Gender.Should().Be(Gender.Unisex);
        variant.Weight.Should().Be(WeightClass.Any);
        var piece = variant.Pieces.Should().ContainSingle().Subject;
        piece.EditorId.Should().Be("whatever");
        piece.Slot.Should().Be("Other");
        piece.MeshPath.Should().Be("meshes/zzzztexture/whatever.nif");
    }

    [Fact]
    public async Task Lod_And_FirstPerson_Alternates_Are_One_Piece_With_Preferred_Path()
    {
        var dir = DonorFolder();
        DonorMeshTreeBuilder.Write(dir,
            "meshes/armor/iron/f/cuirass.nif",
            "meshes/armor/iron/f/cuirass_0.nif",
            "meshes/armor/iron/f/cuirass_1.nif",
            "meshes/armor/iron/f/cuirass_1st.nif");

        var donor = await new DonorClassifier().ClassifyAsync(dir);

        var set = donor.ProvidedSets.Should().ContainSingle().Subject;
        var variant = set.Variants.Should().ContainSingle().Subject;
        variant.Gender.Should().Be(Gender.Female);
        var piece = variant.Pieces.Should().ContainSingle().Subject;
        piece.EditorId.Should().Be("cuirass");
        piece.MeshPath.Should().Be("meshes/armor/iron/f/cuirass_1.nif");
        piece.Slot.Should().Be("Cuirass");

        donor.FileManifest.Should().Contain(e => e.RelativePath == "meshes/armor/iron/f/cuirass.nif");
        donor.FileManifest.Should().Contain(e => e.RelativePath == "meshes/armor/iron/f/cuirass_0.nif");
        donor.FileManifest.Should().Contain(e => e.RelativePath == "meshes/armor/iron/f/cuirass_1.nif");
        donor.FileManifest.Should().Contain(e => e.RelativePath == "meshes/armor/iron/f/cuirass_1st.nif");
    }

    [Fact]
    public async Task Texture_Only_Folder_Yields_No_Sets_And_Does_Not_Crash()
    {
        var dir = DonorFolder();
        DonorMeshTreeBuilder.Write(dir,
            "textures/armor/iron/cuirass.dds",
            "textures/armor/iron/cuirass_n.dds");

        var donor = await new DonorClassifier().ClassifyAsync(dir);

        donor.ProvidedSets.Should().BeEmpty();
        donor.Kind.Should().Be(DonorAssetKind.Unknown);
    }

    [Fact]
    public async Task Weight_Token_In_Folder_Yields_Weight_Class()
    {
        var dir = DonorFolder();
        DonorMeshTreeBuilder.Write(dir, "meshes/armor/heavyiron/f/cuirass.nif");

        var donor = await new DonorClassifier().ClassifyAsync(dir);

        var set = donor.ProvidedSets.Should().ContainSingle().Subject;
        set.Variants.Should().ContainSingle().Which.Weight.Should().Be(WeightClass.Heavy);
    }

    [Fact]
    public async Task Mesh_Only_Donor_Is_Deterministic()
    {
        var dir = DonorFolder();
        DonorMeshTreeBuilder.Write(dir,
            "meshes/armor/iron/f/cuirass.nif",
            "meshes/armor/iron/f/gauntlets.nif",
            "meshes/armor/iron/m/cuirass.nif",
            "textures/armor/iron/cuirass.dds");

        var first = await new DonorClassifier().ClassifyAsync(dir);
        var second = await new DonorClassifier().ClassifyAsync(dir);

        Shape(first).Should().Be(Shape(second));
        first.FileManifest.Should().Equal(second.FileManifest);
    }

    [Fact]
    public async Task Fell_Through_Plugin_With_Meshes_Classifies_Via_Branch2()
    {
        var dir = DonorFolder();
        DonorModBuilder.WriteEmptyEsp(dir);
        DonorMeshTreeBuilder.Write(dir, "meshes/armor/iron/f/cuirass.nif");

        var donor = await new DonorClassifier().ClassifyAsync(dir);

        var set = donor.ProvidedSets.Should().ContainSingle().Subject;
        set.Id.Should().Be("iron");
        var piece = set.Variants.Should().ContainSingle().Subject.Pieces.Should().ContainSingle().Subject;
        piece.MeshPath.Should().Be("meshes/armor/iron/f/cuirass.nif");
    }

    private static string Shape(DonorAsset donor)
    {
        return string.Join("\n", donor.ProvidedSets.Select(s =>
            $"{s.Id}|{s.DisplayName}[" +
            string.Join(";", s.Variants.Select(v =>
                $"{v.Gender}/{v.Weight}[" +
                string.Join(",", v.Pieces.Select(p =>
                    $"{p.EditorId}|{p.Slot}|{p.MeshPath}|{string.Join("+", p.TexturePaths)}")) +
                "]")) +
            "]"));
    }
}