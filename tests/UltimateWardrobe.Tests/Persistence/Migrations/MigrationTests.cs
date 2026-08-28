using FluentAssertions;
using Microsoft.Data.Sqlite;
using UltimateWardrobe.Persistence;
using UltimateWardrobe.Persistence.Migrations;

namespace UltimateWardrobe.Tests.Persistence.Migrations;

/// <summary>
/// Sprint 4.1 - migration engine + versioning + backup: a fresh DB gets <c>M001_Initial</c> applied
/// on open, a DB already at the current version is not re-run, a DB newer than the app is refused
/// (fail-fast), an existing schema is backed up to <c>project.db.bak</c> before an upgrade, and a
/// corrupt or unbackable DB falls back to a typed <see cref="ProjectStoreException"/> without
/// touching the schema.
/// </summary>
public class MigrationTests
{
    private static string NewTempDb()
    {
        var dir = Path.Combine(Path.GetTempPath(), "UW_Migr_" + Guid.NewGuid().ToString("N"));
        return Path.Combine(dir, "project.db");
    }

    private static void DeleteDirectoryRetry(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                Directory.Delete(directory, true);
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Fresh_Database_Applies_M001_On_Open()
    {
        var path = NewTempDb();
        try
        {
            var db = await ProjectDatabase.OpenAsync(path);
            await using var _ = db;

            var version = await new Migrator(Array.Empty<IMigration>()).GetCurrentVersionAsync(db.Connection, CancellationToken.None);
            version.Should().Be(1);

            foreach (var table in new[] { "Project", "Overhaul", "DonorAsset", "PieceMapping", "CatalogCache" })
            {
                var count = await ScalarAsync(db.Connection, $"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='{table}';");
                count.Should().Be(1, $"expected table {table} to exist after M001");
            }

            var mappingCount = M001_SchemaTablesCount(db.Connection);
            mappingCount.Should().Be(6); // SchemaVersion + 5 domain tables
        }
        finally
        {
            DeleteDirectoryRetry(Path.GetDirectoryName(path));
        }
    }

    private static long M001_SchemaTablesCount(SqliteConnection connection)
    {
        // sync helper: just read the count
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table';";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public async Task Existing_Current_Database_Is_Not_ReMigrated()
    {
        var path = NewTempDb();
        try
        {
            var db1 = await ProjectDatabase.OpenAsync(path);
            await using var _ = db1;
            await db1.DisposeAsync();

            var db2 = await ProjectDatabase.OpenAsync(path);
            await using var __ = db2;

            var version = await new Migrator(Array.Empty<IMigration>()).GetCurrentVersionAsync(db2.Connection, CancellationToken.None);
            version.Should().Be(1);

            // Schema untouched and M001 did not run a second time (would have thrown on CREATE TABLE
            // collision); the tables are still present exactly once.
            var count = await ScalarAsync(db2.Connection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Overhaul';");
            count.Should().Be(1);

            // No spare .bak from an idempotent reopen of a current DB.
            File.Exists(path + ".bak").Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryRetry(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public async Task Newer_Database_Is_Refused_Without_Downgrade()
    {
        var path = NewTempDb();
        try
        {
            var db = await ProjectDatabase.OpenAsync(path);
            // Bump beyond the app's max (1) to simulate a DB written by a newer build.
            await using (var command = db.Connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES (99, '01-01-2030');";
                await command.ExecuteNonQueryAsync();
            }
            await db.DisposeAsync();

            var act = async () => await ProjectDatabase.OpenAsync(path);

            (await act.Should().ThrowAsync<ProjectStoreException>())
                .WithMessage("*newer than this app*");
        }
        finally
        {
            DeleteDirectoryRetry(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public async Task Upgrade_Backs_Up_To_Bak_Before_Applying()
    {
        var path = NewTempDb();
        try
        {
            var db = await ProjectDatabase.OpenAsync(path);
            await using var _ = db;

            var migrator = new Migrator(new IMigration[] { new M001_Initial(), new FakeM002() });

            var version = await migrator.MigrateAsync(db.Connection, path, CancellationToken.None);

            version.Should().Be(2);
            File.Exists(path + ".bak").Should().BeTrue();

            var markerCount = await ScalarAsync(db.Connection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='FakeM002Marker';");
            markerCount.Should().Be(1);
        }
        finally
        {
            DeleteDirectoryRetry(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public async Task Corrupt_Database_Throws_Typed_Exception_On_Open()
    {
        var path = NewTempDb();
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(path, "this is not a sqlite database");

            var act = async () => await ProjectDatabase.OpenAsync(path);

            (await act.Should().ThrowAsync<ProjectStoreException>())
                .WithMessage("*project database*");
        }
        finally
        {
            DeleteDirectoryRetry(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public async Task Failed_Backup_Aborts_And_Leaves_Schema_At_Current_Version()
    {
        var path = NewTempDb();
        try
        {
            var db = await ProjectDatabase.OpenAsync(path);
            await using var _ = db;

            // Force the backup to fail by colliding with a directory named project.db.bak.
            Directory.CreateDirectory(path + ".bak");

            var migrator = new Migrator(new IMigration[] { new M001_Initial(), new FakeM002() });

            var act = async () => await migrator.MigrateAsync(db.Connection, path, CancellationToken.None);

            await act.Should().ThrowAsync<ProjectStoreException>();

            // No migration ran: schema still at v1 and the fake table does not exist.
            var version = await new Migrator(Array.Empty<IMigration>()).GetCurrentVersionAsync(db.Connection, CancellationToken.None);
            version.Should().Be(1);

            var markerCount = await ScalarAsync(db.Connection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='FakeM002Marker';");
            markerCount.Should().Be(0);
        }
        finally
        {
            DeleteDirectoryRetry(Path.GetDirectoryName(path));
        }
    }
}
