using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.Tests.Mapping;

/// <summary>
/// Sprint 3.4.1 - determinism: running the same assign sequence over the same synthetic catalog and
/// donor set must produce the same per-set statuses and the same Overhaul progress on every run.
/// Each run builds fresh project/Overhaul/service instances (so GUIDs differ), yet the derived
/// domain state is identical - the guarantee the Phase 6 UI and Phase 5 export rely on.
/// </summary>
public class MappingDeterminismTests
{
    private sealed record RunResult(
        OverhaulProgress Progress,
        ArmorSetStatus SetStatus,
        IReadOnlyList<PieceMapping> Mappings);

    private static RunResult Run()
    {
        var catalog = SyntheticCatalogUniverse.CreateIronCatalog();
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(catalog, PatchPolicy.RequireBoth);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donor);
        var service = new MappingService(project.Library);

        Map(service, overhaul, catalog, donor, Gender.Male, "ArmorIronCuirass", "DonorIronCuirass");
        Map(service, overhaul, catalog, donor, Gender.Male, "ArmorIronGauntlets", "DonorIronGauntlets");
        Map(service, overhaul, catalog, donor, Gender.Female, "ArmorIronCuirassF", "DonorIronCuirassF");
        Map(service, overhaul, catalog, donor, Gender.Female, "ArmorIronGauntletsF", "DonorIronGauntletsF");

        var progress = service.GetOverhaulProgress(overhaul.Mappings, catalog);
        var status = service.GetArmorSetStatus(catalog.Sets[0], overhaul.Mappings);
        return new RunResult(progress, status, overhaul.Mappings.ToList());
    }

    [Fact]
    public void Same_Assign_Sequence_Produces_Same_Statuses_And_Progress_Every_Run()
    {
        var run1 = Run();
        var run2 = Run();
        var run3 = Run();

        run1.SetStatus.Should().Be(run2.SetStatus).And.Be(run3.SetStatus);
        run1.Progress.Should().Be(run2.Progress).And.Be(run3.Progress);

        // The machinery is deterministic too: the per-mapping statuses (the derived domain state)
        // are identical across runs regardless of the freshly generated mapping GUIDs.
        run1.Mappings.Select(m => m.Status).Should().Equal(run2.Mappings.Select(m => m.Status));
        run1.Mappings.Select(m => m.Status).Should().Equal(run3.Mappings.Select(m => m.Status));
    }

    [Fact]
    public void Deterministic_Result_Mirrors_The_Expected_Derived_State()
    {
        // Sanity lock on WHAT the deterministic result is: all four pieces mapped (no NeedsPatch),
        // so the single Iron set reads Mapped and progress counts it there.
        var run = Run();

        run.SetStatus.Should().Be(ArmorSetStatus.Mapped);
        run.Progress.TotalSets.Should().Be(1);
        run.Progress.Mapped.Should().Be(1);
        run.Progress.Done.Should().Be(0);
        run.Progress.InProgress.Should().Be(0);
        run.Progress.NotStarted.Should().Be(0);
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
