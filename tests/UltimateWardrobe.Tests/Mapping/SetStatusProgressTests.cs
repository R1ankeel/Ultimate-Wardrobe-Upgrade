using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.Tests.Mapping;

/// <summary>
/// Sprint 3.3 - per-set <see cref="ArmorSetStatus"/> + Overhaul progress (plan 3.3.1-3.3.4): the
/// NotStarted / InProgress / Mapped / NeedsPatch derivation over a set's pieces x variants, the
/// <c>Done</c> overlay (<see cref="MappingService.SetDone"/> + <see cref="MappingService.GetOverhaulProgress"/>),
/// and the progress sum invariant.
/// </summary>
public class SetStatusProgressTests
{
    private static Catalog Catalog => SyntheticCatalogUniverse.CreateIronCatalog();

    private static (MappingService Service, Project Project, Overhaul Overhaul, Catalog Catalog) NewProject(
        Catalog catalog, PatchPolicy policy = PatchPolicy.Loose)
    {
        var (project, overhaul) = MappingFixtures.CreateOverhaulWithCatalog(catalog, policy);
        return (new MappingService(project.Library), project, overhaul, catalog);
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

    private static Catalog BuildCatalog(int setCount)
    {
        var sets = new List<ArmorSet>();
        for (var i = 0; i < setCount; i++)
        {
            var male = new Variant(Gender.Male, WeightClass.Heavy, new[]
            {
                new Piece($"Piece{i}", (uint)(0x1000 + i), "32 Body", $"A{i}", $"meshes/armor/{i}/a.nif"),
            });
            sets.Add(new ArmorSet($"Set{i}", $"Set {i}", new[] { male }));
        }

        return new Catalog(new VanillaCatalogSource("C:/test"), sets);
    }

    private static PieceMapping BuildMapping(Overhaul overhaul, DonorAsset donor, string setId, string pieceEditor, MappingStatus status)
        => new PieceMapping(
            Guid.NewGuid(), overhaul.Id, setId, pieceEditor, Gender.Male,
            donor.ImportId, $"D{pieceEditor}", "meshes/armor/0/a.nif", status: status);

    [Fact]
    public void Only_Male_Cuirass_Mapped_Is_InProgress()
    {
        var (service, project, overhaul, catalog) = NewProject(Catalog);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donor);
        Map(service, overhaul, catalog, donor, Gender.Male, "ArmorIronCuirass", "DonorIronCuirass");

        service.GetArmorSetStatus(catalog.Sets[0], overhaul.Mappings).Should().Be(ArmorSetStatus.InProgress);
    }

    [Fact]
    public void Both_Genders_Fully_Mapped_Is_Mapped()
    {
        var (service, project, overhaul, catalog) = NewProject(Catalog);
        var donorM = MappingFixtures.CreateIronDonor(project.Id);
        var donorF = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donorM);
        project.Library.Assets.Add(donorF);

        Map(service, overhaul, catalog, donorM, Gender.Male, "ArmorIronCuirass", "DonorIronCuirass");
        Map(service, overhaul, catalog, donorM, Gender.Male, "ArmorIronGauntlets", "DonorIronGauntlets");
        Map(service, overhaul, catalog, donorF, Gender.Female, "ArmorIronCuirassF", "DonorIronCuirassF");
        Map(service, overhaul, catalog, donorF, Gender.Female, "ArmorIronGauntletsF", "DonorIronGauntletsF");

        service.GetArmorSetStatus(catalog.Sets[0], overhaul.Mappings).Should().Be(ArmorSetStatus.Mapped);
    }

    [Fact]
    public void Mapped_Piece_With_NeedsPatch_Status_Is_NeedsPatch()
    {
        var (service, project, overhaul, catalog) = NewProject(Catalog);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donor);
        Map(service, overhaul, catalog, donor, Gender.Male, "ArmorIronCuirass", "DonorIronCuirass");

        var gauntlet = catalog.Sets[0].Variants.First(v => v.Gender == Gender.Male)
            .Pieces.First(p => p.EditorId == "ArmorIronGauntlets");
        var donorGauntlet = donor.ProvidedSets[0].Variants.First(v => v.Gender == Gender.Male)
            .Pieces.First(p => p.EditorId == "DonorIronGauntlets");
        overhaul.Mappings.Add(new PieceMapping(
            Guid.NewGuid(), overhaul.Id, "IronArmor", gauntlet.EditorId, Gender.Male,
            donor.ImportId, donorGauntlet.EditorId, donorGauntlet.MeshPath ?? "",
            status: MappingStatus.NeedsPatch));

        service.GetArmorSetStatus(catalog.Sets[0], overhaul.Mappings).Should().Be(ArmorSetStatus.NeedsPatch);
    }

    [Fact]
    public void Nothing_Mapped_Is_NotStarted()
    {
        var (service, _, _, catalog) = NewProject(Catalog);

        service.GetArmorSetStatus(catalog.Sets[0], Array.Empty<PieceMapping>()).Should().Be(ArmorSetStatus.NotStarted);
    }

    [Fact]
    public void SetDone_Toggles_Done_Bucket_Without_Changing_Derived_Status()
    {
        var (service, project, overhaul, catalog) = NewProject(Catalog);
        var donorM = MappingFixtures.CreateIronDonor(project.Id);
        var donorF = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donorM);
        project.Library.Assets.Add(donorF);
        Map(service, overhaul, catalog, donorM, Gender.Male, "ArmorIronCuirass", "DonorIronCuirass");
        Map(service, overhaul, catalog, donorM, Gender.Male, "ArmorIronGauntlets", "DonorIronGauntlets");
        Map(service, overhaul, catalog, donorF, Gender.Female, "ArmorIronCuirassF", "DonorIronCuirassF");
        Map(service, overhaul, catalog, donorF, Gender.Female, "ArmorIronGauntletsF", "DonorIronGauntletsF");
        var set = catalog.Sets[0];

        service.GetArmorSetStatus(set, overhaul.Mappings).Should().Be(ArmorSetStatus.Mapped);

        var on = service.SetDone(set, overhaul.Mappings, true);
        var onProgress = service.GetOverhaulProgress(overhaul.Mappings, catalog, on);
        onProgress.Done.Should().Be(1);
        onProgress.Mapped.Should().Be(0);

        var off = service.SetDone(set, overhaul.Mappings, false);
        var offProgress = service.GetOverhaulProgress(overhaul.Mappings, catalog, off);
        offProgress.Done.Should().Be(0);
        offProgress.Mapped.Should().Be(1);

        service.GetArmorSetStatus(set, overhaul.Mappings).Should().Be(ArmorSetStatus.Mapped);
    }

    [Fact]
    public void SetDone_On_NotStarted_Set_Does_Not_Reach_Done_Bucket()
    {
        var (service, _, _, catalog) = NewProject(Catalog);
        var set = catalog.Sets[0];

        var on = service.SetDone(set, Array.Empty<PieceMapping>(), true);
        var progress = service.GetOverhaulProgress(Array.Empty<PieceMapping>(), catalog, on);

        progress.Done.Should().Be(0);
        progress.NotStarted.Should().Be(1);
    }

    [Fact]
    public void Overhaul_Progress_Matches_Counts_And_Sum_Invariant()
    {
        const int total = 200;
        var catalog = BuildCatalog(total);
        var project = new Project(Guid.NewGuid(), "big", "C:/Projects/Big");
        var overhaul = new Overhaul(Guid.NewGuid(), "Big", project.Id, catalog.Source);
        var donor = MappingFixtures.CreateIronDonor(project.Id);
        project.Library.Assets.Add(donor);
        var service = new MappingService(project.Library);

        const int mappedCount = 184;
        const int needsPatchCount = 2;
        // 184 sets fully mapped (Piece0..Piece183), 2 sets NeedsPatch (Piece184,185), the rest NotStarted.
        for (var i = 0; i < mappedCount; i++)
        {
            overhaul.Mappings.Add(BuildMapping(overhaul, donor, $"Set{i}", $"Piece{i}", MappingStatus.Mapped));
        }

        for (var i = mappedCount; i < mappedCount + needsPatchCount; i++)
        {
            overhaul.Mappings.Add(BuildMapping(overhaul, donor, $"Set{i}", $"Piece{i}", MappingStatus.NeedsPatch));
        }

        var doneOverrides = new Dictionary<string, bool>();
        foreach (var i in new[] { 1, 2, 3, 4, 5 })
        {
            doneOverrides[$"Set{i}"] = true;
        }

        var progress = service.GetOverhaulProgress(overhaul.Mappings, catalog, doneOverrides);

        progress.TotalSets.Should().Be(total);
        progress.NotStarted.Should().Be(total - mappedCount - needsPatchCount); // 14
        progress.InProgress.Should().Be(0);
        progress.NeedsPatch.Should().Be(needsPatchCount); // 2
        progress.Done.Should().Be(5);
        progress.Mapped.Should().Be(mappedCount - 5); // 179

        progress.DoneFraction.Should().BeApproximately(5.0 / total, 1e-9);
        progress.Remaining.Should().Be(total - 5);

        (progress.Done + progress.Mapped + progress.InProgress + progress.NeedsPatch + progress.NotStarted)
            .Should().Be(total);
    }
}
