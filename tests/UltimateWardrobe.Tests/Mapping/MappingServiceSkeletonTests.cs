using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.Tests.Mapping;

/// <summary>
/// Sprint 3.0.4 skeleton tests: the API shape is stable and the empty / unmapped cases resolve
/// without exceptions. The full CRUD and status derivation suites arrive in Sprints 3.1-3.4.
/// </summary>
public class MappingServiceSkeletonTests
{
    [Fact]
    public void Empty_Catalog_Gives_Zero_Progress()
    {
        var catalog = SyntheticCatalogUniverse.CreateIronCatalog();
        var (project, _) = MappingFixtures.CreateOverhaulWithCatalog(catalog);
        var service = new MappingService(project.Library);
        var emptyCatalog = new Catalog(catalog.Source, Array.Empty<ArmorSet>());

        var progress = service.GetOverhaulProgress(Array.Empty<PieceMapping>(), emptyCatalog);

        progress.TotalSets.Should().Be(0);
        progress.NotStarted.Should().Be(0);
        progress.InProgress.Should().Be(0);
        progress.Mapped.Should().Be(0);
        progress.NeedsPatch.Should().Be(0);
        progress.Done.Should().Be(0);
        progress.DoneFraction.Should().Be(0);
        progress.Remaining.Should().Be(0);
    }

    [Fact]
    public void Unmapped_Iron_Catalog_Is_All_NotStarted()
    {
        var catalog = SyntheticCatalogUniverse.CreateIronCatalog();
        var (project, _) = MappingFixtures.CreateOverhaulWithCatalog(catalog);
        var service = new MappingService(project.Library);

        var progress = service.GetOverhaulProgress(Array.Empty<PieceMapping>(), catalog);

        progress.TotalSets.Should().Be(1);
        progress.NotStarted.Should().Be(1);
        progress.Mapped.Should().Be(0);
        progress.InProgress.Should().Be(0);
        progress.NeedsPatch.Should().Be(0);
        progress.Done.Should().Be(0);
        progress.DoneFraction.Should().Be(0);
        progress.Remaining.Should().Be(1);
    }

    [Fact]
    public void GetArmorSetStatus_Without_Mappings_Is_NotStarted()
    {
        var catalog = SyntheticCatalogUniverse.CreateIronCatalog();
        var (project, _) = MappingFixtures.CreateOverhaulWithCatalog(catalog);
        var service = new MappingService(project.Library);

        var status = service.GetArmorSetStatus(catalog.Sets[0], Array.Empty<PieceMapping>());

        status.Should().Be(ArmorSetStatus.NotStarted);
    }
}
