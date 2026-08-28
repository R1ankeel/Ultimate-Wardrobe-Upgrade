using FluentAssertions;
using UltimateWardrobe.DonorLibrary;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class MeshPathIndexerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"UW_Donor_Mesh_{Guid.NewGuid():N}");

    public MeshPathIndexerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void IndexMeshes_RootLayout_Returns_GameRelative_Paths_With_Meshes_Stem()
    {
        DonorMeshTreeBuilder.Write(_root,
            "meshes/armor/iron/f/cuirass.nif",
            "meshes/armor/iron/m/cuirass.nif");

        var result = new MeshPathIndexer().IndexMeshes(_root);

        result.Should().BeEquivalentTo(
            new[] { "meshes/armor/iron/f/cuirass.nif", "meshes/armor/iron/m/cuirass.nif" },
            opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void IndexMeshes_DataLayout_Strips_The_Data_Prefix()
    {
        DonorMeshTreeBuilder.Write(_root,
            "Data/meshes/armor/iron/f/cuirass.nif",
            "Data/meshes/armor/iron/m/cuirass.nif");

        var result = new MeshPathIndexer().IndexMeshes(_root);

        result.Should().BeEquivalentTo(
            new[] { "meshes/armor/iron/f/cuirass.nif", "meshes/armor/iron/m/cuirass.nif" },
            opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void IndexMeshes_Both_Layouts_Deduplicates_Identical_Game_Paths()
    {
        DonorMeshTreeBuilder.Write(_root,
            "meshes/armor/iron/f/cuirass.nif",
            "Data/meshes/armor/iron/f/cuirass.nif");

        var result = new MeshPathIndexer().IndexMeshes(_root);

        result.Should().ContainSingle().Which.Should().Be("meshes/armor/iron/f/cuirass.nif");
    }

    [Fact]
    public void IndexMeshes_Ignores_Non_Mesh_Files()
    {
        DonorMeshTreeBuilder.Write(_root,
            "meshes/armor/iron/f/cuirass.nif",
            "meshes/armor/iron/f/notes.txt");

        var result = new MeshPathIndexer().IndexMeshes(_root);

        result.Should().ContainSingle().Which.Should().Be("meshes/armor/iron/f/cuirass.nif");
    }

    [Fact]
    public void IndexTextures_Returns_Dds_Game_Relative_Paths()
    {
        DonorMeshTreeBuilder.Write(_root,
            "textures/armor/iron/cuirass.dds",
            "textures/armor/iron/cuirass_n.dds");

        var result = new MeshPathIndexer().IndexTextures(_root);

        result.Should().BeEquivalentTo(
            new[] { "textures/armor/iron/cuirass.dds", "textures/armor/iron/cuirass_n.dds" },
            opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Index_Missing_Folder_Returns_Empty()
    {
        new MeshPathIndexer().IndexMeshes(Path.Combine(_root, "nope")).Should().BeEmpty();
        new MeshPathIndexer().IndexTextures(Path.Combine(_root, "nope")).Should().BeEmpty();
    }

    [Fact]
    public void IndexEmpty_Empty_Root_Returns_Empty()
    {
        new MeshPathIndexer().IndexMeshes(_root).Should().BeEmpty();
        new MeshPathIndexer().IndexTextures(_root).Should().BeEmpty();
    }
}