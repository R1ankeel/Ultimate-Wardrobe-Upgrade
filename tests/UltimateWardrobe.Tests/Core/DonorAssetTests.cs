using FluentAssertions;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.Core;

public class DonorAssetTests
{
    [Fact]
    public void DonorAsset_Creates_With_Defaults()
    {
        var donor = Fixtures.CreateDonorAsset();
        donor.Kind.Should().Be(DonorAssetKind.FullReplacer);
        donor.ProvidedSets.Should().BeEmpty();
        donor.FileManifest.Should().BeEmpty();
        donor.DetectedBodySlideFiles.Should().BeEmpty();
        donor.DetectedPhysicsFiles.Should().BeEmpty();
    }

    [Fact]
    public void DonorProvidedSet_Default_To_Empty_Variants()
    {
        var set = new UltimateWardrobe.Core.Domain.DonorProvidedSet("IronArmor", "Iron Armor");
        set.Variants.Should().BeEmpty();

        var piece = new UltimateWardrobe.Core.Domain.Piece("IronCuirass", 0x12E46, "Body", "IronCuirassAA", "meshes/armor/iron/cuirass.nif");
        var variant = new UltimateWardrobe.Core.Domain.Variant(Gender.Male, WeightClass.Heavy, new[] { piece });
        var withVariants = new UltimateWardrobe.Core.Domain.DonorProvidedSet("IronArmor", "Iron Armor", new[] { variant });
        withVariants.Variants.Should().HaveCount(1);
    }

    [Fact]
    public void DonorFileEntry_Requires_Path_And_NonNegative_Length()
    {
        var entry = new UltimateWardrobe.Core.Domain.DonorFileEntry("meshes/a.nif", 123);
        entry.RelativePath.Should().Be("meshes/a.nif");
        entry.Length.Should().Be(123);

        var act = () => new UltimateWardrobe.Core.Domain.DonorFileEntry("", 1);
        act.Should().Throw<ArgumentException>();

        var neg = () => new UltimateWardrobe.Core.Domain.DonorFileEntry("a.nif", -1);
        neg.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DonorAsset_Throws_On_Empty_Required()
    {
        var act = () => new UltimateWardrobe.Core.Domain.DonorAsset(Guid.Empty, "f.7z", "C:/p", DateTime.UtcNow, "hash");
        act.Should().Throw<ArgumentException>();

        var act2 = () => new UltimateWardrobe.Core.Domain.DonorAsset(Guid.NewGuid(), "", "C:/p", DateTime.UtcNow, "hash");
        act2.Should().Throw<ArgumentException>();

        var act3 = () => new UltimateWardrobe.Core.Domain.DonorAsset(Guid.NewGuid(), "f.7z", "", DateTime.UtcNow, "hash");
        act3.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DonorLibrary_Is_OneToOne_With_Project()
    {
        var project = Fixtures.CreateProject();
        project.Library.ProjectId.Should().Be(project.Id);
        project.Library.Assets.Should().BeEmpty();

        var donor = Fixtures.CreateDonorAsset(project.Id);
        project.Library.Assets.Add(donor);
        project.Library.Assets.Should().HaveCount(1);
    }
}
