using FluentAssertions;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;
using UltimateWardrobe.Tests.Scanner;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class DonorKindClassifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"UW_Donor_Kind_{Guid.NewGuid():N}");

    public DonorKindClassifierTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string KindFolder()
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task<UltimateWardrobe.Core.Domain.DonorAsset> Classify(string dir)
    {
        return await new DonorClassifier().ClassifyAsync(dir);
    }

    [Fact]
    public async Task BodySlide_Only_Folder_Is_BodyConversionPatch()
    {
        var dir = KindFolder();
        DonorMeshTreeBuilder.Write(dir, "CalienteTools/BodySlide/SliderSets/3BBB.osp");

        var donor = await Classify(dir);

        donor.Kind.Should().Be(DonorAssetKind.BodyConversionPatch);
        donor.DetectedBodySlideFiles.Should().ContainSingle().Which.Should().Be("CalienteTools/BodySlide/SliderSets/3BBB.osp");
        donor.DetectedPhysicsFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Physics_Only_Folder_Is_PhysicsPatch()
    {
        var dir = KindFolder();
        DonorMeshTreeBuilder.Write(dir, "SKSE/Plugins/hdtSMP64.dll");

        var donor = await Classify(dir);

        donor.Kind.Should().Be(DonorAssetKind.PhysicsPatch);
        donor.DetectedPhysicsFiles.Should().ContainSingle().Which.Should().Be("SKSE/Plugins/hdtSMP64.dll");
        donor.DetectedBodySlideFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Mesh_Only_Iron_Kit_Is_FullReplacer()
    {
        var dir = KindFolder();
        DonorMeshTreeBuilder.Write(dir,
            "meshes/armor/iron/f/cuirass.nif",
            "meshes/armor/iron/f/gauntlets.nif",
            "meshes/armor/iron/f/boots.nif",
            "meshes/armor/iron/f/helmet.nif");

        var donor = await Classify(dir);

        donor.Kind.Should().Be(DonorAssetKind.FullReplacer);
        donor.DetectedBodySlideFiles.Should().BeEmpty();
        donor.DetectedPhysicsFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Meshes_And_BodySlide_Are_FullReplacer_With_Flag()
    {
        var dir = KindFolder();
        DonorMeshTreeBuilder.Write(dir,
            "meshes/armor/iron/f/cuirass.nif",
            "CalienteTools/BodySlide/SliderSets/3BBB.osp");

        var donor = await Classify(dir);

        donor.Kind.Should().Be(DonorAssetKind.FullReplacer);
        donor.DetectedBodySlideFiles.Should().ContainSingle();
    }

    [Fact]
    public async Task Plugin_And_Physics_Are_FullReplacer_With_Flag()
    {
        var dir = KindFolder();
        SyntheticSkyrimMods.WriteMain(dir);
        DonorMeshTreeBuilder.Write(dir, "SKSE/Plugins/hdtSMP64.dll");

        var donor = await Classify(dir);

        donor.Kind.Should().Be(DonorAssetKind.FullReplacer);
        donor.DetectedPhysicsFiles.Should().ContainSingle().Which.Should().Be("SKSE/Plugins/hdtSMP64.dll");
    }

    [Fact]
    public async Task Mesh_Accessory_Only_Is_Unknown()
    {
        var dir = KindFolder();
        DonorMeshTreeBuilder.Write(dir, "meshes/armor/iron/f/ring.nif");

        var donor = await Classify(dir);

        donor.ProvidedSets.Should().ContainSingle();
        donor.Kind.Should().Be(DonorAssetKind.Unknown);
    }

    [Fact]
    public async Task Mesh_Accessory_With_Sliders_Is_BodyConversionPatch()
    {
        var dir = KindFolder();
        DonorMeshTreeBuilder.Write(dir,
            "meshes/armor/iron/f/ring.nif",
            "CalienteTools/BodySlide/SliderSets/3BBB.osp");

        var donor = await Classify(dir);

        donor.Kind.Should().Be(DonorAssetKind.BodyConversionPatch);
    }

    [Fact]
    public async Task Tri_Under_Detected_Set_Is_Flagged_As_Physics()
    {
        var dir = KindFolder();
        DonorMeshTreeBuilder.Write(dir,
            "meshes/armor/iron/f/cuirass.nif",
            "meshes/armor/iron/f/gauntlets.nif",
            "meshes/armor/iron/f/tongue.tri",
            "meshes/weapons/other.tri");

        var donor = await Classify(dir);

        donor.Kind.Should().Be(DonorAssetKind.FullReplacer);
        donor.DetectedPhysicsFiles.Should().ContainSingle().Which.Should().Be("meshes/armor/iron/f/tongue.tri");
    }
}