using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;
using UltimateWardrobe.Persistence;
using UltimateWardrobe.Tests.Mapping;

namespace UltimateWardrobe.Tests.Persistence;

/// <summary>
/// Sprint 4.3 - <see cref="ProjectStore"/> whole-graph round-trip (plan 4.3.2/4.3.3). A
/// Phase-3-shaped graph (Project + 2 Overhauls + Iron catalog cache + 4 mappings each + 3 DonorAssets
/// incl. one <c>BodyConversionPatch</c> with detected flags, plus an attached body-conversion patch)
/// is <c>SaveAsync</c>'d then <c>LoadAsync</c>'d, and the reloaded graph must deep-equal the original
/// while <see cref="MappingService.GetArmorSetStatus"/> / <see cref="MappingService.GetOverhaulProgress"/>
/// produce identical results on the reloaded library.
/// </summary>
public class ProjectStoreRoundTripTests
{
    private static Catalog IronCatalog => SyntheticCatalogUniverse.CreateIronCatalog();

    [Fact]
    public async Task SaveThenLoad_RoundTrips_FullGraph_Identically()
    {
        var root = TestHelpers.NewTempDir("UW_Store_");
        try
        {
            var dbPath = Path.Combine(root, "project.db");
            var catalog = IronCatalog;

            var (project, overhaulA, overhaulB) = BuildGraph(catalog);

            var saved = Measure(project, overhaulA, overhaulB, catalog);

            await new ProjectStore(dbPath).SaveAsync(project);

            var loaded = await new ProjectStore(dbPath).LoadAsync(dbPath);
            var reloaded = Measure(loaded, loaded.Overhauls[0], loaded.Overhauls[1], catalog);

            loaded.Should().BeEquivalentTo(project, options => options
                .Excluding(p => p.Library)
                .Excluding(p => p.Overhauls));
            loaded.Library.Assets.Should().BeEquivalentTo(project.Library.Assets);
            loaded.Overhauls.Should().BeEquivalentTo(project.Overhauls);

            reloaded.Should().BeEquivalentTo(saved);
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task SaveThenLoad_Keeps_BodyConversionPatch_And_DetectedFlags()
    {
        var root = TestHelpers.NewTempDir("UW_Store_");
        try
        {
            var dbPath = Path.Combine(root, "project.db");
            var catalog = IronCatalog;
            var (project, overhaul, _) = BuildGraph(catalog);

            var patch = project.Library.Assets.First(a => a.Kind == DonorAssetKind.BodyConversionPatch);

            await new ProjectStore(dbPath).SaveAsync(project);

            var loaded = await new ProjectStore(dbPath).LoadAsync(dbPath);
            var loadedPatch = loaded.Library.Assets.First(a => a.ImportId == patch.ImportId);

            loadedPatch.Kind.Should().Be(DonorAssetKind.BodyConversionPatch);
            loadedPatch.DetectedBodySlideFiles.Should().Equal(patch.DetectedBodySlideFiles);
            loadedPatch.DetectedPhysicsFiles.Should().Equal(patch.DetectedPhysicsFiles);

            var mapping = loaded.Overhauls
                .SelectMany(o => o.Mappings)
                .First(m => m.BodyConversionPatchAssetId == patch.ImportId);
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task SaveAsync_Twice_Is_UpsertOnly_No_Duplicate_Rows()
    {
        var root = TestHelpers.NewTempDir("UW_Store_");
        try
        {
            var dbPath = Path.Combine(root, "project.db");
            var (project, _, _) = BuildGraph(IronCatalog);

            // Issue 3: whole-graph save is upsert-only by stable domain id - a second save of the
            // same graph must NOT duplicate any row (no delete-then-reinsert).
            await new ProjectStore(dbPath).SaveAsync(project);
            await new ProjectStore(dbPath).SaveAsync(project);

            var loaded = await new ProjectStore(dbPath).LoadAsync(dbPath);
            loaded.Library.Assets.Should().HaveCount(3);
            loaded.Overhauls.Should().HaveCount(2);
            loaded.Overhauls.Should().OnlyContain(o => o.Mappings.Count == 4);

            loaded.Should().BeEquivalentTo(project, options => options
                .Excluding(p => p.Library)
                .Excluding(p => p.Overhauls));
            loaded.Library.Assets.Should().BeEquivalentTo(project.Library.Assets);
            loaded.Overhauls.Should().BeEquivalentTo(project.Overhauls);
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    private static Snapshot Measure(Project project, Overhaul overhaulA, Overhaul overhaulB, Catalog catalog)
    {
        var service = new MappingService(project.Library);
        var statusA = service.GetArmorSetStatus(catalog.Sets[0], overhaulA.Mappings);
        var statusB = service.GetArmorSetStatus(catalog.Sets[0], overhaulB.Mappings);
        var progressA = service.GetOverhaulProgress(overhaulA.Mappings, catalog);
        var progressB = service.GetOverhaulProgress(overhaulB.Mappings, catalog);
        return new Snapshot(statusA, statusB, progressA, progressB);
    }

    private sealed record Snapshot(
        ArmorSetStatus OverhaulAStatus,
        ArmorSetStatus OverhaulBStatus,
        OverhaulProgress OverhaulAProgress,
        OverhaulProgress OverhaulBProgress);

    private static (Project Project, Overhaul OverhaulA, Overhaul OverhaulB) BuildGraph(Catalog catalog)
    {
        var project = new Project(Guid.NewGuid(), "RoundTripProject", "C:/Projects/RoundTrip");

        var donorM = MappingFixtures.CreateIronDonor(project.Id, "donor-male.7z");
        var donorF = MappingFixtures.CreateIronDonor(project.Id, "donor-female.7z");
        var patch = MappingFixtures.CreateDonorOutput(
            project.Id,
            kind: DonorAssetKind.BodyConversionPatch,
            name: "shapeshift-patch.7z",
            bodySlideFiles: new[] { "BodySlide/Shapeshift/FemaleBody.nif" },
            physicsFiles: new[] { "Physics/Shapeshift/physics.xml" });
        project.Library.Assets.Add(donorM);
        project.Library.Assets.Add(donorF);
        project.Library.Assets.Add(patch);

        var (a, b) = (BuildOverhaul(project.Id, catalog, "Overhaul-A"), BuildOverhaul(project.Id, catalog, "Overhaul-B"));

        MapAll(project.Library, a, catalog, donorM, donorF);
        MapAll(project.Library, b, catalog, donorM, donorF);

        var patchedMapping = a.Mappings[0];
        a.Mappings.Remove(patchedMapping);
        var re = new PieceMapping(
            patchedMapping.Id, patchedMapping.OverhaulId, patchedMapping.TargetArmorSetId,
            patchedMapping.TargetPieceEditorId, patchedMapping.TargetGender, patchedMapping.DonorAssetId,
            patchedMapping.DonorPieceEditorId, patchedMapping.DonorMeshPath,
            bodyConversionPatchAssetId: patch.ImportId,
            status: MappingStatus.Mapped);
        a.Mappings.Add(re);

        project.Overhauls.Add(a);
        project.Overhauls.Add(b);
        return (project, a, b);
    }

    private static Overhaul BuildOverhaul(Guid projectId, Catalog catalog, string name)
    {
        var (_, fixture) = MappingFixtures.CreateOverhaulWithCatalog(catalog, PatchPolicy.RequireBoth, name);
        return new Overhaul(fixture.Id, fixture.Name, projectId, catalog.Source)
        {
            Policy = PatchPolicy.RequireBoth,
            Catalog = catalog,
        };
    }

    private static void MapAll(UltimateWardrobe.Core.Domain.DonorLibrary donors, Overhaul overhaul, Catalog catalog, DonorAsset donorM, DonorAsset donorF)
    {
        var service = new MappingService(donors);
        Map(service, overhaul, catalog, donorM, Gender.Male, "ArmorIronCuirass", "DonorIronCuirass");
        Map(service, overhaul, catalog, donorM, Gender.Male, "ArmorIronGauntlets", "DonorIronGauntlets");
        Map(service, overhaul, catalog, donorF, Gender.Female, "ArmorIronCuirassF", "DonorIronCuirassF");
        Map(service, overhaul, catalog, donorF, Gender.Female, "ArmorIronGauntletsF", "DonorIronGauntletsF");
    }

    private static void Map(
        MappingService service, Overhaul overhaul, Catalog catalog, DonorAsset donor,
        Gender gender, string targetEditor, string donorEditor)
    {
        var target = catalog.Sets[0].Variants.First(v => v.Gender == gender)
            .Pieces.First(p => p.EditorId == targetEditor);
        var donorPiece = donor.ProvidedSets[0].Variants.First(v => v.Gender == gender)
            .Pieces.First(p => p.EditorId == donorEditor);
        service.AssignDonor(overhaul, catalog, donor, target, donorPiece);
    }
}
