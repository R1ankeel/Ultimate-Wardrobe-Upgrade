using FluentAssertions;
using Microsoft.Data.Sqlite;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Persistence;
using UltimateWardrobe.Persistence.Repositories;
using Fixtures = UltimateWardrobe.Tests.Core.Fixtures;

namespace UltimateWardrobe.Tests.Persistence;

/// <summary>
/// Sprint 4.2.4/4.2.6 - PieceMapping upsert-collision (UniqueKey replace), a DB-level FK enforcement
/// test (a mapping referencing a donor that is not in the DB is rejected ONLY because
/// <c>PRAGMA foreign_keys=ON</c> is set on the connection - issue 3), explicit-delete leaves-first
/// ordering, and transaction rollback leaving no partial rows.
/// </summary>
public class RepositoryMappingAndTransactionTests
{
    private static async Task<(Project Project, Overhaul Overhaul, DonorAsset Donor, PieceMapping Mapping)> SeedAsync(RepositoryTestDb test, string donorIdPrefix = "donor")
    {
        var projectRepo = new ProjectRepository(test.Uow);
        var overhaulRepo = new OverhaulRepository(test.Uow);
        var assetRepo = new DonorAssetRepository(test.Uow);
        var mappingRepo = new PieceMappingRepository(test.Uow);

        var project = Fixtures.CreateProject();
        await projectRepo.UpsertAsync(project, CancellationToken.None);

        var overhaul = Fixtures.CreateOverhaul(project);
        await overhaulRepo.UpsertAsync(overhaul, CancellationToken.None);

        var donor = Fixtures.CreateDonorAsset(kind: DonorAssetKind.FullReplacer);
        await assetRepo.UpsertAsync(donor, project.Id, CancellationToken.None);

        var mapping = Fixtures.CreateMapping(overhaul, donor, "IronArmor", "ArmorIronCuirass");
        await mappingRepo.UpsertAsync(mapping, CancellationToken.None);

        return (project, overhaul, donor, mapping);
    }

    [Fact]
    public async Task PieceMapping_Upsert_With_Same_UniqueKey_Replaces_Never_Duplicates()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        var (_, overhaul, donor, original) = await SeedAsync(test);

        var secondDonor = Fixtures.CreateDonorAsset(kind: DonorAssetKind.FullReplacer);
        await new DonorAssetRepository(test.Uow).UpsertAsync(secondDonor, overhaul.ProjectId, CancellationToken.None);

        // Same UniqueKey (overhaul/piece/gender) but a different donor and a fresh row id.
        var replacement = new PieceMapping(
            Guid.NewGuid(), overhaul.Id, original.TargetArmorSetId, original.TargetPieceEditorId,
            original.TargetGender, secondDonor.ImportId, "DonorSecondCuirass", "donor/second.nif",
            status: MappingStatus.Mapped);
        await new PieceMappingRepository(test.Uow).UpsertAsync(replacement, CancellationToken.None);

        var rows = await new PieceMappingRepository(test.Uow).GetByOverhaulAsync(overhaul.Id, CancellationToken.None);
        rows.Should().ContainSingle();
        rows[0].DonorAssetId.Should().Be(secondDonor.ImportId);
        rows[0].DonorPieceEditorId.Should().Be("DonorSecondCuirass");
        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM PieceMapping;")).Should().Be(1);
    }

    [Fact]
    public async Task ForeignKey_Rejects_Mapping_To_Donor_Not_In_Database()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        var projectRepo = new ProjectRepository(test.Uow);
        var overhaulRepo = new OverhaulRepository(test.Uow);
        var project = Fixtures.CreateProject();
        await projectRepo.UpsertAsync(project, CancellationToken.None);
        var overhaul = Fixtures.CreateOverhaul(project);
        await overhaulRepo.UpsertAsync(overhaul, CancellationToken.None);

        // A borrowed/foreign donor GUID that was never inserted into this DB.
        var badMapping = new PieceMapping(
            Guid.NewGuid(), overhaul.Id, "IronArmor", "ArmorIronCuirass", Gender.Male,
            Guid.NewGuid(), "Donor", "donor/x.nif");

        var act = async () => await new PieceMappingRepository(test.Uow).UpsertAsync(badMapping, CancellationToken.None);

        // Only enforced because PRAGMA foreign_keys=ON is set on the connection (issue 3).
        await act.Should().ThrowAsync<SqliteException>().WithMessage("*FOREIGN KEY*");
    }

    [Fact]
    public async Task Delete_Overhaul_With_Children_First_Fails_ForeignKey()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        var (_, overhaul, _, _) = await SeedAsync(test);

        var act = async () => await new OverhaulRepository(test.Uow).DeleteAsync(overhaul.Id, CancellationToken.None);

        // PieceMapping/CatalogCache still reference the Overhaul, so a leaves-first delete is required.
        await act.Should().ThrowAsync<SqliteException>().WithMessage("*FOREIGN KEY*");
    }

    [Fact]
    public async Task Delete_Leaves_First_Succeeds_For_Overhaul_And_Donor()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        var (project, overhaul, donor, mapping) = await SeedAsync(test);

        // Leave nodes first: the mapping rows (the only children) referencing both the overhaul and
        // the donor, plus the catalog cache referencing the overhaul.
        await new CatalogCacheRepository(test.Uow).UpsertAsync(overhaul.Id, Fixtures.CreateCatalog(), DateTime.UtcNow, CancellationToken.None);
        await new PieceMappingRepository(test.Uow).DeleteAsync(mapping.Id, CancellationToken.None);
        await new CatalogCacheRepository(test.Uow).DeleteAsync(overhaul.Id, CancellationToken.None);

        // Now the parents can be deleted without an FK violation.
        await new OverhaulRepository(test.Uow).DeleteAsync(overhaul.Id, CancellationToken.None);
        await new DonorAssetRepository(test.Uow).DeleteAsync(donor.ImportId, CancellationToken.None);
        await new ProjectRepository(test.Uow).UpsertAsync(project, CancellationToken.None); // keep project for clarity

        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM PieceMapping;")).Should().Be(0);
        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM Overhaul;")).Should().Be(0);
        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM DonorAsset;")).Should().Be(0);
    }

    [Fact]
    public async Task BeginAsync_Reissues_DeferForeignKeys_On_Every_Transaction()
    {
        await using var test = await RepositoryTestDb.CreateAsync();

        // defer_foreign_keys is a TRANSACTION-level pragma (plan 4.3.1 implementation note): SQLite
        // resets it to OFF after every commit/rollback, so the UnitOfWork must re-issue it inside
        // EACH new transaction on the long-lived connection. A 0 on the 2nd/3rd iteration would
        // prove the bug (the pragma was only set once at open).
        for (var i = 0; i < 3; i++)
        {
            await test.Uow.BeginAsync(CancellationToken.None);
            (await TestHelpers.ScalarAsync(test.Uow, "PRAGMA defer_foreign_keys;")).Should().Be(1);
            await test.Uow.CommitAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Transaction_Rollback_Leaves_No_Partial_Rows()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        await test.Uow.BeginAsync(CancellationToken.None);

        var (project, overhaul, donor, _) = await SeedAsync(test);

        // A failing statement inside the same transaction. NOTE: FK violations are deferred to
        // COMMIT under PRAGMA defer_foreign_keys=ON (the 4.3.1 note), so use an immediate NOT NULL
        // violation to force a mid-transaction failure deterministically.
        await using (var command = test.Uow.Connection.CreateCommand())
        {
            command.Transaction = test.Uow.Transaction;
            command.CommandText = "INSERT INTO Project (Id, Name, RootPath, SchemaVersion, CreatedAt, ModifiedAt) VALUES ('bad', NULL, 'r', 1, '1', '1');";
            var failing = async () => await command.ExecuteNonQueryAsync();
            await failing.Should().ThrowAsync<SqliteException>();
        }

        await test.Uow.RollbackAsync(CancellationToken.None);

        // Nothing from the transaction persists.
        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM Project;")).Should().Be(0);
        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM Overhaul;")).Should().Be(0);
        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM DonorAsset;")).Should().Be(0);
        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM PieceMapping;")).Should().Be(0);

        _ = project; _ = overhaul; _ = donor;
    }

    [Fact]
    public async Task Transaction_Commit_Persists_Rows()
    {
        await using var test = await RepositoryTestDb.CreateAsync();
        await test.Uow.BeginAsync(CancellationToken.None);

        await SeedAsync(test);

        await test.Uow.CommitAsync(CancellationToken.None);

        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM Project;")).Should().Be(1);
        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM Overhaul;")).Should().Be(1);
        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM DonorAsset;")).Should().Be(1);
        (await TestHelpers.ScalarAsync(test.Uow, "SELECT count(*) FROM PieceMapping;")).Should().Be(1);
    }
}
