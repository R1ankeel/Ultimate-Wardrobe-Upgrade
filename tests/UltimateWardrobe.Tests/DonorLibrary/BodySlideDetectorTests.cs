using FluentAssertions;
using UltimateWardrobe.DonorLibrary;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class BodySlideDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"UW_Donor_BS_{Guid.NewGuid():N}");
    private readonly BodySlideDetector _detector = new();

    public BodySlideDetectorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Detect_Covers_SliderSets_SliderGroups_And_BodySlide_Root_Xml()
    {
        DonorMeshTreeBuilder.Write(_root,
            "CalienteTools/BodySlide/SliderSets/3BBB.osp",
            "CalienteTools/BodySlide/SliderSets/Tac0/az.osp",
            "CalienteTools/BodySlide/SliderGroups/all.xml",
            "CalienteTools/BodySlide/SliderGroups/sub/g.xml",
            "CalienteTools/BodySlide/presets.xml");

        var result = _detector.Detect(_root);

        result.Should().Equal(
            "CalienteTools/BodySlide/SliderGroups/all.xml",
            "CalienteTools/BodySlide/SliderGroups/sub/g.xml",
            "CalienteTools/BodySlide/SliderSets/3BBB.osp",
            "CalienteTools/BodySlide/SliderSets/Tac0/az.osp",
            "CalienteTools/BodySlide/presets.xml");
    }

    [Fact]
    public void Detect_Excludes_Deeper_And_Unrelated_Files()
    {
        DonorMeshTreeBuilder.Write(_root,
            "CalienteTools/BodySlide/SliderSets/3BBB.osp",
            "CalienteTools/BodySlide/DropdownData/dd.xml",
            "CalienteTools/BodySlide/DropdownData/deep/dd2.xml",
            "CalienteTools/BodySlide/readme.txt",
            "CalienteTools/BodySlide2/SliderSets/x.osp",
            "SKSE/Plugins/config.xml");

        var result = _detector.Detect(_root);

        result.Should().ContainSingle().Which.Should().Be("CalienteTools/BodySlide/SliderSets/3BBB.osp");
    }

    [Fact]
    public void Detect_Data_Layout_Strips_The_Data_Prefix()
    {
        DonorMeshTreeBuilder.Write(_root, "Data/CalienteTools/BodySlide/SliderSets/3BBB.osp");

        var result = _detector.Detect(_root);

        result.Should().ContainSingle().Which.Should().Be("CalienteTools/BodySlide/SliderSets/3BBB.osp");
    }

    [Fact]
    public void Detect_Both_Layouts_Deduplicates_Identical_Paths()
    {
        DonorMeshTreeBuilder.Write(_root,
            "CalienteTools/BodySlide/SliderSets/3BBB.osp",
            "Data/CalienteTools/BodySlide/SliderSets/3BBB.osp");

        var result = _detector.Detect(_root);

        result.Should().ContainSingle();
    }

    [Fact]
    public void Detect_Missing_Folder_Returns_Empty()
    {
        _detector.Detect(Path.Combine(_root, "nope")).Should().BeEmpty();
    }
}