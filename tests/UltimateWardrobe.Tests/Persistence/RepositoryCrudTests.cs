using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Persistence;
using UltimateWardrobe.Persistence.Repositories;
using Fixtures = UltimateWardrobe.Tests.Core.Fixtures;

namespace UltimateWardrobe.Tests.Persistence;

/// <summary>
/// Sprint 4.2 - repository CRUD + value round-trip for Project, Overhaul, DonorAsset and
/// CatalogCache (4.2.1-4.2.3, 4.2.5). Each upsert is stable by domain <c>Id</c> and updates in
/// place; reads reconstruct the domain value with the stored fields.
/// </summary>
public class RepositoryCrudTests
{
    [Fact]
    public async Task ProjectRepository_Upsert_Then_Get()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        var repo = new ProjectRepository(test.Uow);
        var project = Fixtures.CreateProject("Test", "C:/Projects/Test");

        await repo.UpsertAsync(project, CancellationToken.None);
        var loaded = await repo.GetAsync(project.Id, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(project.Id);
        loaded.Name.Should().Be("Test");
        loaded.RootPath.Should().Be("C:/Projects/Test");
        loaded.SchemaVersion.Should().Be(project.SchemaVersion);
        loaded.CreatedAt.Should().Be(project.CreatedAt);
        loaded.ModifiedAt.Should().Be(project.ModifiedAt);
    }

    [Fact]
    public async Task ProjectRepository_Upsert_Updates_In_Place()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        var repo = new ProjectRepository(test.Uow);
        var project = Fixtures.CreateProject("A", "C:/Projects/A");
        await repo.UpsertAsync(project, CancellationToken.None);

        var renamed = new Project(project.Id, "B", "C:/Projects/B", project.SchemaVersion)
        {
            CreatedAt = project.CreatedAt,
            ModifiedAt = project.ModifiedAt,
        };
        await repo.UpsertAsync(renamed, CancellationToken.None);

        var loaded = await repo.GetAsync(project.Id, CancellationToken.None);
        loaded!.Name.Should().Be("B");
        loaded.RootPath.Should().Be("C:/Projects/B");

        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM Project;")).Should().Be(1);
    }

    [Fact]
    public async Task ProjectRepository_Get_Missing_Returns_Null()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        var repo = new ProjectRepository(test.Uow);

        var loaded = await repo.GetAsync(Guid.NewGuid(), CancellationToken.None);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task OverhaulRepository_Upsert_Get_ByProject_And_Get()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        var projectRepo = new ProjectRepository(test.Uow);
        var overhaulRepo = new OverhaulRepository(test.Uow);
        var project = Fixtures.CreateProject();
        await projectRepo.UpsertAsync(project, CancellationToken.None);

        var source = Fixtures.CreateStorySource();
        var overhaul = new Overhaul(Guid.NewGuid(), "Vigilant", project.Id, source)
        {
            Policy = PatchPolicy.RequireBoth,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        await overhaulRepo.UpsertAsync(overhaul, CancellationToken.None);

        var byProject = await overhaulRepo.GetByProjectAsync(project.Id, CancellationToken.None);
        byProject.Should().ContainSingle();
        var loaded = byProject[0];
        loaded.Id.Should().Be(overhaul.Id);
        loaded.Name.Should().Be("Vigilant");
        loaded.ProjectId.Should().Be(project.Id);
        loaded.Policy.Should().Be(PatchPolicy.RequireBoth);
        loaded.Source.Should().BeOfType<StoryModCatalogSource>();
        ((StoryModCatalogSource)loaded.Source).MainPlugin.Should().Be("Vigilant.esp");
        loaded.CreatedAt.Should().Be(overhaul.CreatedAt);
        loaded.ModifiedAt.Should().Be(overhaul.ModifiedAt);

        var byId = await overhaulRepo.GetAsync(overhaul.Id, CancellationToken.None);
        byId.Should().NotBeNull();
        byId!.Name.Should().Be("Vigilant");
    }

    [Fact]
    public async Task OverhaulRepository_Delete_Removes_Row()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        var projectRepo = new ProjectRepository(test.Uow);
        var overhaulRepo = new OverhaulRepository(test.Uow);
        var project = Fixtures.CreateProject();
        await projectRepo.UpsertAsync(project, CancellationToken.None);
        var overhaul = Fixtures.CreateOverhaul(project);
        await overhaulRepo.UpsertAsync(overhaul, CancellationToken.None);

        await overhaulRepo.DeleteAsync(overhaul.Id, CancellationToken.None);

        (await overhaulRepo.GetAsync(overhaul.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task DonorAssetRepository_RoundTrips_Rich_Asset()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        var projectRepo = new ProjectRepository(test.Uow);
        var assetRepo = new DonorAssetRepository(test.Uow);
        var project = Fixtures.CreateProject();
        await projectRepo.UpsertAsync(project, CancellationToken.None);

        var variant = new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece("DonorCuirass", 0x00001234, "32 Body", "AA_Donor", "donor/cuirass.nif") });
        var asset = new DonorAsset(
            Guid.NewGuid(),
            "bodypatch.7z",
            "C:/Project/Source/Extracted",
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            "hash789",
            DonorAssetKind.BodyConversionPatch,
            providedSets: new[] { new DonorProvidedSet("setA", "Donor Set", new[] { variant }) },
            fileManifest: new[] { new DonorFileEntry("meshes/donor/cuirass.nif", 42L) },
            detectedBodySlideFiles: new[] { "assets/body/0.nif" },
            detectedPhysicsFiles: new[] { "physics/1.hkx" });

        await assetRepo.UpsertAsync(asset, project.Id, CancellationToken.None);

        var byProject = await assetRepo.GetByProjectAsync(project.Id, CancellationToken.None);
        byProject.Should().ContainSingle();
        var loaded = byProject[0];
        loaded.ImportId.Should().Be(asset.ImportId);
        loaded.OriginalFileName.Should().Be("bodypatch.7z");
        loaded.ExtractedPath.Should().Be("C:/Project/Source/Extracted");
        loaded.ArchiveHash.Should().Be("hash789");
        loaded.Kind.Should().Be(DonorAssetKind.BodyConversionPatch);
        loaded.ImportedAt.Should().Be(asset.ImportedAt);
        loaded.DetectedBodySlideFiles.Should().Equal("assets/body/0.nif");
        loaded.DetectedPhysicsFiles.Should().Equal("physics/1.hkx");

        loaded.ProvidedSets.Should().ContainSingle().Which.Variants.Should().ContainSingle().Which.Gender.Should().Be(Gender.Female);
        loaded.FileManifest.Should().ContainSingle().Which.Length.Should().Be(42L);

        var byId = await assetRepo.GetAsync(asset.ImportId, CancellationToken.None);
        byId.Should().NotBeNull();

        await assetRepo.DeleteAsync(asset.ImportId, CancellationToken.None);
        (await assetRepo.GetAsync(asset.ImportId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task CatalogCacheRepository_Upsert_Get_Delete()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        var projectRepo = new ProjectRepository(test.Uow);
        var overhaulRepo = new OverhaulRepository(test.Uow);
        var cacheRepo = new CatalogCacheRepository(test.Uow);
        var project = Fixtures.CreateProject();
        await projectRepo.UpsertAsync(project, CancellationToken.None);
        var overhaul = Fixtures.CreateOverhaul(project);
        await overhaulRepo.UpsertAsync(overhaul, CancellationToken.None);

        var catalog = Fixtures.CreateCatalog();
        var cachedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        await cacheRepo.UpsertAsync(overhaul.Id, catalog, cachedAt, CancellationToken.None);

        var loaded = await cacheRepo.GetAsync(overhaul.Id, CancellationToken.None);
        loaded.Should().NotBeNull();
        loaded!.Value.CachedAt.Should().Be(cachedAt);
        loaded.Value.Catalog.Sets.Should().ContainSingle().Which.Id.Should().Be("IronArmor");

        await cacheRepo.DeleteAsync(overhaul.Id, CancellationToken.None);
        (await cacheRepo.GetAsync(overhaul.Id, CancellationToken.None)).Should().BeNull();
    }
}
