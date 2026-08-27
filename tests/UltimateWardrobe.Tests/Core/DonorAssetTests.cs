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
