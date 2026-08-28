using FluentAssertions;
using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;
using UltimateWardrobe.Mapping;
using UltimateWardrobe.Persistence;
using UltimateWardrobe.Tests.Mapping;
using Xunit.Abstractions;

namespace UltimateWardrobe.Tests.Persistence;

/// <summary>
/// Sprint 4.4.1 - Integration-gated end-to-end persistence spot-check, gated behind the
/// <c>Integration</c> category and auto-skipped (with an output note) whenever
/// <c>ModsForTests/Armor</c> has no "Red Hood - HIMBO" archive (the
/// <see cref="UltimateWardrobe.Tests.DonorLibrary.RealDonorIntegrationTests"/> pattern). It runs the
/// full Phase 4 loop over a REAL donor: create Project -> synthesize the Iron catalog -> import +
/// classify Red Hood - HIMBO (the esp-less branch-2 fixture that classifies as
/// <see cref="DonorAssetKind.BodyConversionPatch"/> with real BodySlide + physics flags, recorded in
/// <c>Docs/donor-library.md</c>) -> assign synthetic FullReplacer donors and ATTACH the real donor
/// as the body-conversion patch layer -> <see cref="ProjectStore.SaveAsync"/> -> reopen in a fresh
/// <see cref="ProjectDatabase"/> -> <see cref="ProjectStore.LoadAsync"/> -> assert the reloaded graph
/// deep-equals the original, <see cref="MappingService.GetArmorSetStatus"/> /
/// <see cref="MappingService.GetOverhaulProgress"/> are identical, and the extracted donor folder
/// path still resolves. Cleans <c>%TEMP%/UW_Donor_*</c> + the test <c>project.db</c>/dir in
/// <c>finally</c>.
/// </summary>
[Trait("Category", "Integration")]
public class ProjectStoreIntegrationTests
{
    private const string GameRoot = @"D:\Skymod\Stock Game";

    private static string ArmorDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ModsForTests", "Armor"));

    private static string? FindArmorArchive(string namePart)
    {
        if (!Directory.Exists(ArmorDir))
        {
            return null;
        }

        return Directory.EnumerateFiles(ArmorDir)
            .FirstOrDefault(f => Path.GetFileName(f).Contains(namePart, StringComparison.OrdinalIgnoreCase));
    }

    private readonly ITestOutputHelper _output;

    public ProjectStoreIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SaveLoad_RoundTrips_RealDonor_Graph_With_Identical_Statuses()
    {
        var archive = FindArmorArchive("Red Hood - HIMBO");
        if (archive is null || !File.Exists(archive))
        {
            _output.WriteLine("Skipped: no 'Red Hood - HIMBO' archive under ModsForTests/Armor.");
            return;
        }

        // Red Hood - HIMBO is esp-less (branch 2): the vanilla hint only enriches classification when
        // the game root is present - absent, the classification still runs unchanged.
        var catalog = SyntheticCatalogUniverse.CreateIronCatalog();
        var hint = Directory.Exists(GameRoot)
            ? new Catalog(new VanillaCatalogSource(GameRoot), Array.Empty<ArmorSet>())
            : null;

        var donorDest = Path.Combine(Path.GetTempPath(), "UW_Donor_Persist_" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(Path.GetTempPath(), "UW_Persist_Db_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(donorDest);
        Directory.CreateDirectory(projectDir);

        try
        {
            // 1. Import + classify the REAL donor into the project lib.
            var imported = await new DonorImportService().ImportAsync(archive, donorDest);
            var realAsset = await new DonorClassifier().ClassifyAsync(imported.ExtractedPath, hint);

            _output.WriteLine(
                $"[store-integration] {Path.GetFileName(archive)} -> Kind={realAsset.Kind} | sets={realAsset.ProvidedSets.Count}" +
                $" | bodySlide={realAsset.DetectedBodySlideFiles.Count} physics={realAsset.DetectedPhysicsFiles.Count}" +
                $" | files={realAsset.FileManifest.Count} | dest={realAsset.ExtractedPath}");

            Assert.NotEmpty(realAsset.FileManifest);
            Assert.Equal(DonorAssetKind.BodyConversionPatch, realAsset.Kind);
            Assert.NotEmpty(realAsset.DetectedPhysicsFiles);

            // 2. Project graph: synthetic FullReplacer donors carry the assignments; the REAL donor is
            // attached as the body-conversion patch layer of the male cuirass mapping.
            var project = new Project(Guid.NewGuid(), "IntegrationPersistence", Path.Combine(projectDir, "proj"));
            var donorM = MappingFixtures.CreateIronDonor(project.Id, "donor-male.7z");
            var donorF = MappingFixtures.CreateIronDonor(project.Id, "donor-female.7z");
            project.Library.Assets.Add(donorM);
            project.Library.Assets.Add(donorF);
            project.Library.Assets.Add(realAsset);

            var overhaul = new Overhaul(Guid.NewGuid(), "RealDonorSpotCheck", project.Id, catalog.Source)
            {
                Catalog = catalog,
            };
            project.Overhauls.Add(overhaul);

            var service = new MappingService(project.Library);
            Map(service, overhaul, catalog, donorM, Gender.Male, "ArmorIronCuirass", "DonorIronCuirass");
            Map(service, overhaul, catalog, donorM, Gender.Male, "ArmorIronGauntlets", "DonorIronGauntlets");
            Map(service, overhaul, catalog, donorF, Gender.Female, "ArmorIronCuirassF", "DonorIronCuirassF");
            Map(service, overhaul, catalog, donorF, Gender.Female, "ArmorIronGauntletsF", "DonorIronGauntletsF");

            var cuirassMapping = overhaul.Mappings.First(m =>
                m.TargetPieceEditorId == "ArmorIronCuirass" && m.TargetGender == Gender.Male);
            service.AttachPatch(overhaul, cuirassMapping, realAsset, PatchKind.Body);

            var before = Measure(service, catalog, overhaul);

            // 3. Save the whole graph, then reopen in a FRESH ProjectDatabase before LoadAsync.
            var dbPath = Path.Combine(projectDir, "project.db");
            await new ProjectStore(dbPath).SaveAsync(project);
            Directory.Exists(realAsset.ExtractedPath).Should().BeTrue();

            await using (var reopen = await ProjectDatabase.OpenAsync(dbPath))
            {
                await using var command = reopen.Connection.CreateCommand();
                command.CommandText = "SELECT count(*) FROM Project;";
                Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(1);
            }

            var loaded = await new ProjectStore(dbPath).LoadAsync(dbPath);
            var loadedOverhaul = loaded.Overhauls.Single();

            // 4. Deep equality of the reloaded graph + MappingService parity on the reloaded data.
            loaded.Should().BeEquivalentTo(project, options => options
                .Excluding(p => p.Library)
                .Excluding(p => p.Overhauls));
            loaded.Library.Assets.Should().BeEquivalentTo(project.Library.Assets);
            loaded.Overhauls.Should().BeEquivalentTo(project.Overhauls);

            var after = Measure(new MappingService(loaded.Library), loadedOverhaul.Catalog!, loadedOverhaul);
            after.Should().BeEquivalentTo(before);

            var loadedPatch = loaded.Library.Assets.First(a => a.ImportId == realAsset.ImportId);
            loadedPatch.Kind.Should().Be(DonorAssetKind.BodyConversionPatch);
            loadedPatch.DetectedBodySlideFiles.Should().Equal(realAsset.DetectedBodySlideFiles);
            loadedPatch.DetectedPhysicsFiles.Should().Equal(realAsset.DetectedPhysicsFiles);
            loadedPatch.ExtractedPath.Should().Be(realAsset.ExtractedPath);
            Directory.Exists(loadedPatch.ExtractedPath).Should().BeTrue();
            loadedOverhaul.Mappings.Should().Contain(m => m.BodyConversionPatchAssetId == realAsset.ImportId);
        }
        finally
        {
            try { Directory.Delete(donorDest, true); } catch { }
            TestHelpers.DeleteDirectoryRetry(projectDir);
        }
    }

    private static Snapshot Measure(MappingService service, Catalog catalog, Overhaul overhaul)
    {
        var status = service.GetArmorSetStatus(catalog.Sets[0], overhaul.Mappings);
        var progress = service.GetOverhaulProgress(overhaul.Mappings, catalog);
        return new Snapshot(status, progress);
    }

    private sealed record Snapshot(ArmorSetStatus Status, OverhaulProgress Progress);

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