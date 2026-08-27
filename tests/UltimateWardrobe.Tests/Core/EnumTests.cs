using FluentAssertions;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.Core;

public class EnumTests
{
    [Fact]
    public void ArchiveFormat_Has_Unknown_Zero()
    {
        ((int)ArchiveFormat.Unknown).Should().Be(0);
        Enum.IsDefined(typeof(ArchiveFormat), ArchiveFormat.Unknown).Should().BeTrue();
    }

    [Fact]
    public void All_Enums_Have_Unknown_Zero()
    {
        ((int)Gender.Unknown).Should().Be(0);
        ((int)WeightClass.Unknown).Should().Be(0);
        ((int)ArmorSetStatus.Unknown).Should().Be(0);
        ((int)MappingStatus.Unknown).Should().Be(0);
        ((int)CatalogSourceKind.Unknown).Should().Be(0);
        ((int)DonorAssetKind.Unknown).Should().Be(0);
        ((int)BodyType.Unknown).Should().Be(0);
        ((int)PhysicsType.Unknown).Should().Be(0);
    }

    [Theory]
    [InlineData("Male", Gender.Male)]
    [InlineData("Female", Gender.Female)]
    [InlineData("Unisex", Gender.Unisex)]
    public void Gender_String_RoundTrip(string name, Gender expected)
    {
        Enum.TryParse<Gender>(name, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
        parsed.ToString().Should().Be(name);
    }

    [Theory]
    [InlineData("NotStarted", ArmorSetStatus.NotStarted)]
    [InlineData("NeedsPatch", ArmorSetStatus.NeedsPatch)]
    [InlineData("Done", ArmorSetStatus.Done)]
    public void ArmorSetStatus_RoundTrip(string name, ArmorSetStatus expected)
    {
        Enum.Parse<ArmorSetStatus>(name).Should().Be(expected);
    }

    [Fact]
    public void ArchiveFormat_RoundTrip()
    {
        foreach (var v in Enum.GetValues<ArchiveFormat>())
        {
            var s = v.ToString();
            Enum.Parse<ArchiveFormat>(s).Should().Be(v);
        }
    }

    [Fact]
    public void BodyType_Includes_Common_Types()
    {
        Enum.IsDefined(typeof(BodyType), BodyType.Vanilla).Should().BeTrue();
        Enum.IsDefined(typeof(BodyType), BodyType.HIMBO).Should().BeTrue();
        Enum.IsDefined(typeof(BodyType), BodyType.ThreeBA).Should().BeTrue();
    }
}
