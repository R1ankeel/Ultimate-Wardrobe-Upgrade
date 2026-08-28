using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class DonorKindDetectorTests
{
    private static readonly IReadOnlyList<string> NoFlags = Array.Empty<string>();

    private static DonorProvidedSet Set(params string[] slots)
    {
        var pieces = slots.Select(s => new Piece($"x{s}", 0, s)).ToList();
        return new DonorProvidedSet("iron", "Iron", new[] { new Variant(Gender.Unisex, WeightClass.Any, pieces) });
    }

    [Fact]
    public void Branch1_Sets_Are_FullReplacer_Even_Without_A_Body_Piece()
    {
        DonorKindDetector.Derive([Set("Ring")], setsBroughtViaBranch1: true, NoFlags, NoFlags)
            .Should().Be(DonorAssetKind.FullReplacer);
    }

    [Fact]
    public void Branch1_With_Zero_Sets_Is_Not_FullReplacer()
    {
        DonorKindDetector.Derive([], setsBroughtViaBranch1: true, NoFlags, NoFlags)
            .Should().Be(DonorAssetKind.Unknown);
    }

    [Fact]
    public void Branch2_Set_With_Body_Piece_Is_FullReplacer()
    {
        DonorKindDetector.Derive([Set("Cuirass", "Gauntlets")], setsBroughtViaBranch1: false, NoFlags, NoFlags)
            .Should().Be(DonorAssetKind.FullReplacer);
    }

    [Fact]
    public void Branch2_Accessory_Sets_Fall_Through_To_Flags()
    {
        DonorKindDetector.Derive([Set("Ring")], setsBroughtViaBranch1: false, ["CalienteTools/BodySlide/SliderSets/3BBB.osp"], NoFlags)
            .Should().Be(DonorAssetKind.BodyConversionPatch);

        DonorKindDetector.Derive([Set("Ring")], setsBroughtViaBranch1: false, NoFlags, ["SKSE/Plugins/hdtSMP64.dll"])
            .Should().Be(DonorAssetKind.PhysicsPatch);
    }

    [Fact]
    public void SliderSets_Win_Over_Physics_When_Both_Are_Present()
    {
        DonorKindDetector.Derive([], setsBroughtViaBranch1: false, ["slider.osp"], ["hdtSMP64.dll"])
            .Should().Be(DonorAssetKind.BodyConversionPatch);
    }

    [Theory]
    [InlineData(false, false, false, DonorAssetKind.Unknown)]
    [InlineData(false, true, false, DonorAssetKind.BodyConversionPatch)]
    [InlineData(false, false, true, DonorAssetKind.PhysicsPatch)]
    public void Empty_Sets_With_Only_Flags_Map_To_The_Flag_Kind(bool branch1, bool sliders, bool physics, DonorAssetKind expected)
    {
        DonorKindDetector.Derive(
            [], branch1,
            sliders ? ["slider.osp"] : NoFlags,
            physics ? ["hdtSMP64.dll"] : NoFlags)
            .Should().Be(expected);
    }
}