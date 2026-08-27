using FluentAssertions;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.Core;

public class PieceMappingTests
{
    [Fact]
    public void PieceMapping_Creates_With_UniqueKey()
    {
        var project = Fixtures.CreateProject();
        var overhaul = Fixtures.CreateOverhaul(project);
        var donor = Fixtures.CreateDonorAsset(project.Id);
        project.Library.Assets.Add(donor);

        var mapping = Fixtures.CreateMapping(overhaul, donor);
        mapping.UniqueKey.Should().Be($"{overhaul.Id}:{mapping.TargetPieceEditorId}:{mapping.TargetGender}");
        mapping.Status.Should().Be(MappingStatus.Mapped);
    }

    [Fact]
    public void PieceMapping_Throws_On_Empty_Required_Fields()
    {
        var donorId = Guid.NewGuid();
        var ovlId = Guid.NewGuid();
        var act = () => new UltimateWardrobe.Core.Domain.PieceMapping(Guid.Empty, ovlId, "Set", "Piece", Gender.Male, donorId, "DonorPiece", "mesh.nif");
        act.Should().Throw<ArgumentException>().WithParameterName("id");

        var act2 = () => new UltimateWardrobe.Core.Domain.PieceMapping(Guid.NewGuid(), Guid.Empty, "Set", "Piece", Gender.Male, donorId, "DonorPiece", "mesh.nif");
        act2.Should().Throw<ArgumentException>();

        var act3 = () => new UltimateWardrobe.Core.Domain.PieceMapping(Guid.NewGuid(), ovlId, "", "Piece", Gender.Male, donorId, "DonorPiece", "mesh.nif");
        act3.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateCrossProject_Passes_When_Donor_Belongs_To_Project()
    {
        var project = Fixtures.CreateProject();
        var overhaul = Fixtures.CreateOverhaul(project);
        var donor = Fixtures.CreateDonorAsset(project.Id);
        project.Library.Assets.Add(donor);

        var mapping = Fixtures.CreateMapping(overhaul, donor);
        var act = () => mapping.ValidateCrossProject(project.Library.Assets);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateCrossProject_Throws_When_Donor_From_Other_Project()
    {
        var project = Fixtures.CreateProject();
        var otherProjectId = Guid.NewGuid();
        var overhaul = Fixtures.CreateOverhaul(project);
        var donorFromOther = Fixtures.CreateDonorAsset(otherProjectId);

        // project library does NOT contain donorFromOther
        var mapping = Fixtures.CreateMapping(overhaul, donorFromOther);
        var act = () => mapping.ValidateCrossProject(project.Library.Assets);
        act.Should().Throw<InvalidOperationException>().WithMessage("*does not belong*");
    }

    [Fact]
    public void ValidateCrossProject_Throws_For_Patch_From_Other_Project()
    {
        var project = Fixtures.CreateProject();
        var overhaul = Fixtures.CreateOverhaul(project);
        var donor = Fixtures.CreateDonorAsset(project.Id);
        var patchFromOther = Fixtures.CreateDonorAsset(Guid.NewGuid(), DonorAssetKind.BodyConversionPatch);
        project.Library.Assets.Add(donor);

        var mapping = new UltimateWardrobe.Core.Domain.PieceMapping(
            Guid.NewGuid(), overhaul.Id, "IronArmor", "ArmorIronCuirass", Gender.Male,
            donor.ImportId, "DonorPiece", "mesh.nif",
            bodyConversionPatchAssetId: patchFromOther.ImportId);

        var act = () => mapping.ValidateCrossProject(project.Library.Assets);
        act.Should().Throw<InvalidOperationException>().WithMessage("*BodyConversionPatch*");
    }

    [Fact]
    public void Two_Mappings_Same_Target_Should_Have_Same_UniqueKey_Different_Id()
    {
        var project = Fixtures.CreateProject();
        var overhaul = Fixtures.CreateOverhaul(project);
        var donor1 = Fixtures.CreateDonorAsset(project.Id);
        var donor2 = Fixtures.CreateDonorAsset(project.Id);

        var m1 = new UltimateWardrobe.Core.Domain.PieceMapping(Guid.NewGuid(), overhaul.Id, "Set", "PieceA", Gender.Male, donor1.ImportId, "DP1", "m1.nif");
        var m2 = new UltimateWardrobe.Core.Domain.PieceMapping(Guid.NewGuid(), overhaul.Id, "Set", "PieceA", Gender.Male, donor2.ImportId, "DP2", "m2.nif");

        m1.UniqueKey.Should().Be(m2.UniqueKey);
        m1.Id.Should().NotBe(m2.Id);
        // Future DB will enforce UNIQUE(OverhaulId, TargetPieceEditorId, Gender) - domain models allow two objects but DB must reject second
    }
}
