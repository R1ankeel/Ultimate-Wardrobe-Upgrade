using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.Tests.Mapping;

/// <summary>
/// Sprint 3.1 - MappingService CRUD + validation (plan 3.1.1-3.1.4): assign / patch / unassign /
/// detach over in-memory data with the Phase 0.2 cross-project invariant, patch-kind checks, the
/// patch-as-main-donor reject, and uniqueness by <see cref="PieceMapping.UniqueKey"/>.
/// </summary>
public class MappingServiceCrudTests
{
    private static readonly Catalog Catalog = SyntheticCatalogUniverse.CreateIronCatalog();

    private static Piece IronPiece(Gender gender, string editorId)
        => Catalog.Sets.SelectMany(s => s.Variants)
            .Where(v => v.Gender == gender)
            .SelectMany(v => v.Pieces)
            .First(p => p.EditorId == editorId);

    private static Piece DonorPiece(DonorAsset donor, string editorId)
        => donor.ProvidedSets.SelectMany(s => s.Variants)
            .SelectMany(v => v.Pieces)
            .First(p => p.EditorId == editorId);

    [Fact]
    public void AssignDonor_Maps_Male_And_Female_In_One_Set_With_Different_Donors()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donor7 = MappingFixtures.CreateIronDonor(project.Id);
        var donor8 = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donor7);
        project.Library.Assets.Add(donor8);
        var service = new MappingService(project.Library);

        var maleMapping = service.AssignDonor(
            overhaul, Catalog, donor7,
            IronPiece(Gender.Male, "ArmorIronCuirass"),
            DonorPiece(donor7, "DonorIronCuirass"));
        var femaleMapping = service.AssignDonor(
            overhaul, Catalog, donor8,
            IronPiece(Gender.Female, "ArmorIronCuirassF"),
            DonorPiece(donor8, "DonorIronCuirassF"));

        overhaul.Mappings.Should().HaveCount(2);
        overhaul.Mappings.Select(m => m.UniqueKey).Should().OnlyHaveUniqueItems();

        maleMapping.TargetGender.Should().Be(Gender.Male);
        maleMapping.DonorAssetId.Should().Be(donor7.ImportId);
        maleMapping.DonorPieceEditorId.Should().Be("DonorIronCuirass");
        maleMapping.DonorMeshPath.Should().Be("armor/iron/m/cuirass.nif");
        maleMapping.Status.Should().Be(MappingStatus.Mapped);

        femaleMapping.TargetGender.Should().Be(Gender.Female);
        femaleMapping.DonorAssetId.Should().Be(donor8.ImportId);
        femaleMapping.DonorMeshPath.Should().Be("armor/iron/f/cuirass.nif");
    }

    [Fact]
    public void AssignDonor_Same_Target_Replaces_Not_Duplicates()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donor7 = MappingFixtures.CreateIronDonor(project.Id);
        var donor9 = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donor7);
        project.Library.Assets.Add(donor9);
        var service = new MappingService(project.Library);
        var target = IronPiece(Gender.Male, "ArmorIronCuirass");

        service.AssignDonor(overhaul, Catalog, donor7, target, DonorPiece(donor7, "DonorIronCuirass"));
        service.AssignDonor(overhaul, Catalog, donor9, target, DonorPiece(donor9, "DonorIronCuirass"));

        overhaul.Mappings.Should().HaveCount(1);
        overhaul.Mappings.Single().DonorAssetId.Should().Be(donor9.ImportId);
        overhaul.Mappings.Single().UniqueKey.Should().Be($"{overhaul.Id}:ArmorIronCuirass:{Gender.Male}");
    }

    [Fact]
    public void AttachPatch_Body_And_Physics_Read_Back()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        var bodyPatch = MappingFixtures.CreateDonorOutput(
            project.Id, DonorAssetKind.BodyConversionPatch, "body.7z",
            bodySlideFiles: new[] { "CalienteTools/BodySlide/SliderSets/3BBB.osp" });
        var physicsPatch = MappingFixtures.CreateDonorOutput(
            project.Id, DonorAssetKind.PhysicsPatch, "physics.7z",
            physicsFiles: new[] { "SKSE/Plugins/hdtSMP64.dll" });
        project.Library.Assets.Add(donor);
        project.Library.Assets.Add(bodyPatch);
        project.Library.Assets.Add(physicsPatch);
        var service = new MappingService(project.Library);

        var mapping = service.AssignDonor(
            overhaul, Catalog, donor,
            IronPiece(Gender.Male, "ArmorIronCuirass"),
            DonorPiece(donor, "DonorIronCuirass"));

        service.AttachPatch(overhaul, mapping, bodyPatch, PatchKind.Body);
        service.AttachPatch(overhaul, mapping, physicsPatch, PatchKind.Physics);

        var result = overhaul.Mappings.Single();
        result.BodyConversionPatchAssetId.Should().Be(bodyPatch.ImportId);
        result.PhysicsPatchAssetId.Should().Be(physicsPatch.ImportId);
    }

    [Fact]
    public void AttachPatch_Rejects_FullReplacer_As_Body_Patch()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        var replacer = MappingFixtures.CreateIronDonor(project.Id, "another-replacer.7z");
        project.Library.Assets.Add(donor);
        project.Library.Assets.Add(replacer);
        var service = new MappingService(project.Library);
        var mapping = service.AssignDonor(
            overhaul, Catalog, donor,
            IronPiece(Gender.Male, "ArmorIronCuirass"),
            DonorPiece(donor, "DonorIronCuirass"));

        var act = () => service.AttachPatch(overhaul, mapping, replacer, PatchKind.Body);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Kind*");
        overhaul.Mappings.Single().BodyConversionPatchAssetId.Should().BeNull();
    }

    [Fact]
    public void AttachPatch_Rejects_Physics_Kind_As_Body_Patch()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        var physicsPatch = MappingFixtures.CreateDonorOutput(
            project.Id, DonorAssetKind.PhysicsPatch, "physics.7z",
            physicsFiles: new[] { "SKSE/Plugins/hdtSMP64.dll" });
        project.Library.Assets.Add(donor);
        project.Library.Assets.Add(physicsPatch);
        var service = new MappingService(project.Library);
        var mapping = service.AssignDonor(
            overhaul, Catalog, donor,
            IronPiece(Gender.Male, "ArmorIronCuirass"),
            DonorPiece(donor, "DonorIronCuirass"));

        var act = () => service.AttachPatch(overhaul, mapping, physicsPatch, PatchKind.Body);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Kind*");
        overhaul.Mappings.Single().BodyConversionPatchAssetId.Should().BeNull();
        overhaul.Mappings.Single().PhysicsPatchAssetId.Should().BeNull();
    }

    [Fact]
    public void AttachPatch_Rejects_Patch_From_Another_Project()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donor);
        var service = new MappingService(project.Library);
        var mapping = service.AssignDonor(
            overhaul, Catalog, donor,
            IronPiece(Gender.Male, "ArmorIronCuirass"),
            DonorPiece(donor, "DonorIronCuirass"));

        var (otherProject, _) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var patchFromOther = MappingFixtures.CreateDonorOutput(
            otherProject.Id, DonorAssetKind.BodyConversionPatch, "body.7z",
            bodySlideFiles: new[] { "CalienteTools/BodySlide/SliderSets/3BBB.osp" });

        var act = () => service.AttachPatch(overhaul, mapping, patchFromOther, PatchKind.Body);

        act.Should().Throw<InvalidOperationException>().WithMessage("*does not belong*");
        overhaul.Mappings.Single().BodyConversionPatchAssetId.Should().BeNull();
    }

    [Fact]
    public void AssignDonor_Rejects_Donor_From_Another_Project()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var service = new MappingService(project.Library);

        var (otherProject, _) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donorFromOther = MappingFixtures.CreateIronDonor(otherProject.Id);

        var act = () => service.AssignDonor(
            overhaul, Catalog, donorFromOther,
            IronPiece(Gender.Male, "ArmorIronCuirass"),
            DonorPiece(donorFromOther, "DonorIronCuirass"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*does not belong*");
        overhaul.Mappings.Should().BeEmpty();
    }

    [Fact]
    public void AssignDonor_Rejects_Patch_Kind_As_Main_Donor()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var service = new MappingService(project.Library);

        var bodyPatch = MappingFixtures.CreateDonorOutput(
            project.Id, DonorAssetKind.BodyConversionPatch, "body.7z",
            bodySlideFiles: new[] { "CalienteTools/BodySlide/SliderSets/3BBB.osp" });
        project.Library.Assets.Add(bodyPatch);

        var act = () => service.AssignDonor(
            overhaul, Catalog, bodyPatch,
            IronPiece(Gender.Male, "ArmorIronCuirass"),
            new Piece("DonorIronCuirass", 0x20012E46, "32 Body", "DA", "armor/iron/m/cuirass.nif"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot be used as the main donor*");
        overhaul.Mappings.Should().BeEmpty();
    }

    [Fact]
    public void AssignDonor_Empty_Donor_Mesh_Path_Throws_Ctor_Guard()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donor);
        var service = new MappingService(project.Library);
        var noMeshPiece = new Piece("DonorIronCuirass", 0x20012E46, "32 Body");

        var act = () => service.AssignDonor(
            overhaul, Catalog, donor,
            IronPiece(Gender.Male, "ArmorIronCuirass"),
            noMeshPiece);

        act.Should().Throw<ArgumentException>().WithParameterName("donorMeshPath");
        overhaul.Mappings.Should().BeEmpty();
    }

    [Fact]
    public void AssignDonor_Unknown_Target_Throws()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donor);
        var service = new MappingService(project.Library);
        var unknownTarget = new Piece("NotInCatalogPiece", 0xDEADBEEF, "32 Body", "DA", "a/b.nif");

        var act = () => service.AssignDonor(
            overhaul, Catalog, donor,
            unknownTarget,
            DonorPiece(donor, "DonorIronCuirass"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not found in any catalog set*");
        overhaul.Mappings.Should().BeEmpty();
    }

    [Fact]
    public void Unassign_Removes_Mapping()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donor);
        var service = new MappingService(project.Library);
        var mapping = service.AssignDonor(
            overhaul, Catalog, donor,
            IronPiece(Gender.Male, "ArmorIronCuirass"),
            DonorPiece(donor, "DonorIronCuirass"));

        service.Unassign(overhaul, mapping);

        overhaul.Mappings.Should().BeEmpty();
    }

    [Fact]
    public void Unassign_Missing_Mapping_Throws()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donor);
        var service = new MappingService(project.Library);
        var mapping = service.AssignDonor(
            overhaul, Catalog, donor,
            IronPiece(Gender.Male, "ArmorIronCuirass"),
            DonorPiece(donor, "DonorIronCuirass"));
        service.Unassign(overhaul, mapping);

        var act = () => service.Unassign(overhaul, mapping);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not part of Overhaul*");
        overhaul.Mappings.Should().BeEmpty();
    }

    [Fact]
    public void DetachPatch_Clears_Single_Layer()
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        var bodyPatch = MappingFixtures.CreateDonorOutput(
            project.Id, DonorAssetKind.BodyConversionPatch, "body.7z",
            bodySlideFiles: new[] { "CalienteTools/BodySlide/SliderSets/3BBB.osp" });
        var physicsPatch = MappingFixtures.CreateDonorOutput(
            project.Id, DonorAssetKind.PhysicsPatch, "physics.7z",
            physicsFiles: new[] { "SKSE/Plugins/hdtSMP64.dll" });
        project.Library.Assets.Add(donor);
        project.Library.Assets.Add(bodyPatch);
        project.Library.Assets.Add(physicsPatch);
        var service = new MappingService(project.Library);
        var mapping = service.AssignDonor(
            overhaul, Catalog, donor,
            IronPiece(Gender.Male, "ArmorIronCuirass"),
            DonorPiece(donor, "DonorIronCuirass"));
        service.AttachPatch(overhaul, mapping, bodyPatch, PatchKind.Body);
        service.AttachPatch(overhaul, mapping, physicsPatch, PatchKind.Physics);

        service.DetachPatch(overhaul, overhaul.Mappings.Single(), PatchKind.Body);

        var result = overhaul.Mappings.Single();
        result.BodyConversionPatchAssetId.Should().BeNull();
        result.PhysicsPatchAssetId.Should().Be(physicsPatch.ImportId);
        overhaul.Mappings.Should().HaveCount(1);
    }
}
