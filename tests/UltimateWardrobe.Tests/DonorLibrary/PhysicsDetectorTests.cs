using FluentAssertions;
using UltimateWardrobe.DonorLibrary;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class PhysicsDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"UW_Donor_Ph_{Guid.NewGuid():N}");
    private readonly PhysicsDetector _detector = new();

    public PhysicsDetectorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Detect_Covers_Skse_Plugins_Engine_Files_And_Cbpc_Configs()
    {
        DonorMeshTreeBuilder.Write(_root,
            "SKSE/Plugins/hdtSMP64.dll",
            "SKSE/Plugins/hdtSMP.xml",
            "SKSE/Plugins/config.xml",
            "SKSE/Plugins/CBPC.json",
            "SKSE/Plugins/subevity/deeper.json",
            "SKSE/Plugins/readme.txt");

        var result = _detector.Detect(_root);

        result.Should().Equal(
            "SKSE/Plugins/CBPC.json",
            "SKSE/Plugins/config.xml",
            "SKSE/Plugins/hdtSMP.xml",
            "SKSE/Plugins/hdtSMP64.dll",
            "SKSE/Plugins/subevity/deeper.json");
    }

    [Fact]
    public void Detect_Token_In_File_Name_Flags_Anywhere()
    {
        DonorMeshTreeBuilder.Write(_root,
            "meshes/hdtSMP physics.nif",
            "meshes/MyCBPC.ini",
            "textures/plain.dds");

        var result = _detector.Detect(_root);

        result.Should().Equal(
            "meshes/MyCBPC.ini",
            "meshes/hdtSMP physics.nif");
    }

    [Fact]
    public void Detect_Tri_Only_Under_Detected_Mesh_Set_Folders()
    {
        DonorMeshTreeBuilder.Write(_root,
            "meshes/armor/iron/f/tongue.tri",
            "meshes/armor/iron/m/tongue.tri",
            "meshes/weapons/other.tri");

        var under = _detector.Detect(_root, new[] { "meshes/armor/iron/f/cuirass.nif" });

        under.Should().ContainSingle().Which.Should().Be("meshes/armor/iron/f/tongue.tri");
    }

    [Fact]
    public void Detect_Tri_Without_Set_Mesh_Paths_Is_Not_Flagged()
    {
        DonorMeshTreeBuilder.Write(_root, "meshes/armor/iron/f/tongue.tri");

        _detector.Detect(_root).Should().BeEmpty();
    }

    [Fact]
    public void Detect_Data_Layout_Strips_The_Data_Prefix()
    {
        DonorMeshTreeBuilder.Write(_root, "Data/SKSE/Plugins/hdtSMP64.dll");

        var result = _detector.Detect(_root);

        result.Should().ContainSingle().Which.Should().Be("SKSE/Plugins/hdtSMP64.dll");
    }

    [Fact]
    public void Detect_Missing_Folder_Returns_Empty()
    {
        _detector.Detect(Path.Combine(_root, "nope")).Should().BeEmpty();
    }
}