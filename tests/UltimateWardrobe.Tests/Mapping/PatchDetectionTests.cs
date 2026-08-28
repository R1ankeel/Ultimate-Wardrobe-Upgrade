using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.Tests.Mapping;

/// <summary>
/// Sprint 3.2 - patch requirement detection + recommendation (plan 3.2.1-3.2.4): deterministic
/// <see cref="MappingService.NeedFor"/>/<see cref="MappingService.GetStatus"/> driven by the combined
/// donor-or-attached-patch Phase 2 flags plus <see cref="Overhaul.Policy"/>, the
/// <see cref="MappingService.BodyMarkerFromPath"/> body-token table, and deterministic
/// <see cref="MappingService.RecommendPatches"/> ordering.
/// </summary>
public class PatchDetectionTests
{
    private static Catalog Catalog => SyntheticCatalogUniverse.CreateIronCatalog();

    private static (MappingService Service, Project Project, Overhaul Overhaul, PieceMapping Mapping, DonorAsset Donor) MapIron(
        DonorAsset donor,
        PatchPolicy policy = PatchPolicy.Loose)
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(Catalog, policy);
        project.Library.Assets.Add(donor);
        var service = new MappingService(project.Library);

        var maleCuirass = Catalog.Sets
            .SelectMany(s => s.Variants)
            .Where(v => v.Gender == Gender.Male)
            .SelectMany(v => v.Pieces)
            .First(p => p.EditorId == "ArmorIronCuirass");
        var donorCuirass = donor.ProvidedSets[0].Variants[0].Pieces
            .First(p => p.EditorId == "DonorIronCuirass");

        var mapping = service.AssignDonor(overhaul, Catalog, donor, maleCuirass, donorCuirass);
        return (service, project, overhaul, mapping, donor);
    }

    [Fact]
    public void FullReplacer_No_BodySlide_Under_Loose_Has_No_Demand()
    {
        var donor = MappingFixtures.CreateIronDonor(Guid.NewGuid());
        var (service, _, _, mapping, donorAlias) = MapIron(donor);

        service.NeedFor(mapping, donorAlias, policy: PatchPolicy.Loose).Should().Be(PatchRequirement.None);
        service.GetStatus(mapping, donorAlias, policy: PatchPolicy.Loose).Should().Be(MappingStatus.Mapped);
    }

    [Fact]
    public void Under_RequireBodyConversion_No_BodySlide_Needs_Body_Patch()
    {
        var donor = MappingFixtures.CreateIronDonor(Guid.NewGuid());
        var (service, _, _, mapping, donorAlias) = MapIron(donor, PatchPolicy.RequireBodyConversion);

        service.NeedFor(mapping, donorAlias, policy: PatchPolicy.RequireBodyConversion)
            .Should().Be(PatchRequirement.Body);
        service.GetStatus(mapping, donorAlias, policy: PatchPolicy.RequireBodyConversion)
            .Should().Be(MappingStatus.NeedsPatch);
    }

    [Fact]
    public void Donor_With_BodySlide_Satisfies_RequireBodyConversion()
    {
        var donor = MappingFixtures.CreateDonorOutput(
            Guid.NewGuid(),
            bodySlideFiles: new[] { "CalienteTools/BodySlide/SliderSets/3BBB.osp" },
            providedSets: new[] { MappingFixtures.CreateDonorIronSet() });
        var (service, _, _, mapping, donorAlias) = MapIron(donor, PatchPolicy.RequireBodyConversion);

        service.NeedFor(mapping, donorAlias, policy: PatchPolicy.RequireBodyConversion)
            .Should().Be(PatchRequirement.None);
        service.GetStatus(mapping, donorAlias, policy: PatchPolicy.RequireBodyConversion)
            .Should().Be(MappingStatus.Mapped);
    }

    [Fact]
    public void Attached_Physics_Patch_Satisfies_Physics_When_Donor_Has_None()
    {
        var projectId = Guid.NewGuid();
        var donor = MappingFixtures.CreateIronDonor(projectId);
        var physicsPatch = MappingFixtures.CreateDonorOutput(
            projectId, DonorAssetKind.PhysicsPatch, "physics.7z",
            physicsFiles: new[] { "SKSE/Plugins/hdtSMP64.dll" });
        var (service, _, _, mapping, donorAlias) = MapIron(donor, PatchPolicy.RequirePhysics);

        service.NeedFor(mapping, donorAlias, patchAssetPhysics: physicsPatch, policy: PatchPolicy.RequirePhysics)
            .Should().Be(PatchRequirement.None);
        service.GetStatus(mapping, donorAlias, patchAssetPhysics: physicsPatch, policy: PatchPolicy.RequirePhysics)
            .Should().Be(MappingStatus.Mapped);
    }

    [Fact]
    public void Under_RequirePhysics_No_Physics_Flags_Needs_Physics_Patch()
    {
        var donor = MappingFixtures.CreateIronDonor(Guid.NewGuid());
        var (service, _, _, mapping, donorAlias) = MapIron(donor, PatchPolicy.RequirePhysics);

        service.NeedFor(mapping, donorAlias, policy: PatchPolicy.RequirePhysics)
            .Should().Be(PatchRequirement.Physics);
        service.GetStatus(mapping, donorAlias, policy: PatchPolicy.RequirePhysics)
            .Should().Be(MappingStatus.NeedsPatch);
    }

    [Fact]
    public void Under_RequireBoth_No_Flags_Needs_Both_Patches()
    {
        var donor = MappingFixtures.CreateIronDonor(Guid.NewGuid());
        var (service, _, _, mapping, donorAlias) = MapIron(donor, PatchPolicy.RequireBoth);

        service.NeedFor(mapping, donorAlias, policy: PatchPolicy.RequireBoth)
            .Should().Be(PatchRequirement.Both);
        service.GetStatus(mapping, donorAlias, policy: PatchPolicy.RequireBoth)
            .Should().Be(MappingStatus.NeedsPatch);
    }

    [Fact]
    public void BodySlide_Only_Patch_As_Body_Layer_Satisfies_RequireBodyConversion()
    {
        var projectId = Guid.NewGuid();
        var donor = MappingFixtures.CreateIronDonor(projectId);
        var bodyPatch = MappingFixtures.CreateDonorOutput(
            projectId, DonorAssetKind.BodyConversionPatch, "body.7z",
            bodySlideFiles: new[] { "CalienteTools/BodySlide/SliderSets/3BBB.osp" });
        var (service, _, _, mapping, donorAlias) = MapIron(donor, PatchPolicy.RequireBodyConversion);

        service.NeedFor(mapping, donorAlias, patchAssetBody: bodyPatch, policy: PatchPolicy.RequireBodyConversion)
            .Should().Be(PatchRequirement.None);
        service.GetStatus(mapping, donorAlias, patchAssetBody: bodyPatch, policy: PatchPolicy.RequireBodyConversion)
            .Should().Be(MappingStatus.Mapped);
    }

    [Fact]
    public void BodyMarkerFromPath_Detects_Tokens()
    {
        MappingService.BodyMarkerFromPath("meshes/armor/x/3ba/file.nif").Should().Be(BodyType.ThreeBA);
        MappingService.BodyMarkerFromPath("meshes/armor/x/3baf/file.nif").Should().Be(BodyType.ThreeBA);
        MappingService.BodyMarkerFromPath("meshes/armor/x/cbbe/file.nif").Should().Be(BodyType.CBBE);
        MappingService.BodyMarkerFromPath("meshes/armor/x/bhunp/file.nif").Should().Be(BodyType.BHUNP);
        MappingService.BodyMarkerFromPath("meshes/armor/x/himbo/file.nif").Should().Be(BodyType.HIMBO);
        MappingService.BodyMarkerFromPath("meshes/armor/x/unp/file.nif").Should().Be(BodyType.BHUNP);
        MappingService.BodyMarkerFromPath("meshes/armor/x/unpb/file.nif").Should().Be(BodyType.BHUNP);
    }

    [Fact]
    public void BodyMarkerFromPath_No_Token_Is_Null()
    {
        MappingService.BodyMarkerFromPath("meshes/armor/x/file.nif").Should().BeNull();
        MappingService.BodyMarkerFromPath(null).Should().BeNull();
        MappingService.BodyMarkerFromPath("").Should().BeNull();
    }

    [Fact]
    public void BodyMarker_In_Path_Satisfies_RequireBodyConversion_Without_BodySlide()
    {
        var projectId = Guid.NewGuid();
        var donor = MappingFixtures.CreateDonorOutput(
            projectId,
            providedSets: new[]
            {
                new DonorProvidedSet("DonorIron", "Donor Iron", new[]
                {
                    new Variant(Gender.Male, WeightClass.Heavy, new[]
                    {
                        new Piece("DonorIronCuirass", 0x20012E46, "32 Body", "DA_IronCuirass", "meshes/armor/x/3ba/cuirass.nif"),
                    }),
                }),
            });
        var (service, _, _, mapping, donorAlias) = MapIron(donor, PatchPolicy.RequireBodyConversion);

        service.NeedFor(mapping, donorAlias, policy: PatchPolicy.RequireBodyConversion)
            .Should().Be(PatchRequirement.None);
    }

    [Fact]
    public void RecommendPatches_Returns_Matching_Kinds_In_Deterministic_Order()
    {
        var projectId = Guid.NewGuid();
        var library = new UltimateWardrobe.Core.Domain.DonorLibrary(projectId);
        var body1 = MappingFixtures.CreateDonorOutput(projectId, DonorAssetKind.BodyConversionPatch, "body-1.7z", bodySlideFiles: new[] { "a.osp" });
        var body2 = MappingFixtures.CreateDonorOutput(projectId, DonorAssetKind.BodyConversionPatch, "body-2.7z", bodySlideFiles: new[] { "b.osp" });
        var physics = MappingFixtures.CreateDonorOutput(projectId, DonorAssetKind.PhysicsPatch, "phys.7z", physicsFiles: new[] { "hdtSMP64.dll" });
        var replacer = MappingFixtures.CreateIronDonor(projectId);
        library.Assets.Add(physics);
        library.Assets.Add(replacer);
        library.Assets.Add(body1);
        library.Assets.Add(body2);
        var service = new MappingService(library);

        var both = service.RecommendPatches(library, PatchRequirement.Both);
        both.Should().HaveCount(3);
        both[0].Kind.Should().Be(DonorAssetKind.BodyConversionPatch);
        both[1].Kind.Should().Be(DonorAssetKind.BodyConversionPatch);
        both[2].Kind.Should().Be(DonorAssetKind.PhysicsPatch);
        both.Where(a => a.Kind == DonorAssetKind.BodyConversionPatch).Select(a => a.ImportId).Should().BeInAscendingOrder();

        var bodyOnly = service.RecommendPatches(library, PatchRequirement.Body);
        bodyOnly.Should().HaveCount(2);
        bodyOnly.Select(a => a.ImportId).Should().BeInAscendingOrder();

        var physicsOnly = service.RecommendPatches(library, PatchRequirement.Physics);
        physicsOnly.Should().ContainSingle(a => a.ImportId == physics.ImportId);

        service.RecommendPatches(library, PatchRequirement.None).Should().BeEmpty();

        service.RecommendPatches(library, PatchRequirement.Both).Select(a => a.ImportId)
            .Should().Equal(service.RecommendPatches(library, PatchRequirement.Both).Select(a => a.ImportId));
    }

    [Fact]
    public void Unmapped_Piece_Is_Pending()
    {
        var service = new MappingService(new UltimateWardrobe.Core.Domain.DonorLibrary(Guid.NewGuid()));
        var donor = MappingFixtures.CreateIronDonor(Guid.NewGuid());

        service.GetStatus(null, donor).Should().Be(MappingStatus.Pending);
    }
}
