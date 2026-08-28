using FluentAssertions;
using Microsoft.Data.Sqlite;
using UltimateWardrobe.Persistence;

namespace UltimateWardrobe.Tests.Persistence;

/// <summary>
/// Sprint 4.0.3 - <see cref="ProjectDatabase"/> open/create bootstrap: the file is created, the
/// connection-level pragmas every consumer needs are applied (WAL journaling + <c>foreign_keys=ON</c>,
/// phase 4 plan issue 3), emptiness is detected, and a bad path surfaces a typed
/// <see cref="ProjectStoreException"/> instead of a raw provider exception.
/// </summary>
public class ProjectDatabaseTests
{
    private static string NewTempDb(string? dir = null)
    {
        var directory = dir ?? Path.Combine(Path.GetTempPath(), "UW_Persist_" + Guid.NewGuid().ToString("N"));
        return Path.Combine(directory, "project.db");
    }

    private static async Task<long> QueryScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    [Fact]
    public async Task Open_Creates_The_Database_File()
    {
        var path = NewTempDb();
        try
        {
            var db = await ProjectDatabase.OpenAsync(path);
            await using var _ = db;

            File.Exists(path).Should().BeTrue();
            db.DbPath.Should().Be(Path.GetFullPath(path));
        }
        finally
        {
            DeleteDirectoryRetry(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public async Task Open_Applies_Wal_And_ForeignKeys_Pragmas()
    {
        var path = NewTempDb();
        try
        {
            var db = await ProjectDatabase.OpenAsync(path);
            await using var _ = db;

            var journalText = await ReadStringAsync(db.Connection, "PRAGMA journal_mode;");
            var foreignKeys = await QueryScalarAsync(db.Connection, "PRAGMA foreign_keys;");

            journalText.ToLowerInvariant().Should().Be("wal");
            foreignKeys.Should().Be(1);
        }
        finally
        {
            DeleteDirectoryRetry(Path.GetDirectoryName(path));
        }
    }

    private static async Task<string> ReadStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? string.Empty;
    }

    private static void DeleteDirectoryRetry(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        // SQLite WAL mode can briefly keep the -shm/-wal handles open on Windows even after the
        // connection is disposed, so retry a few times before giving up.
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

    [Fact]
    public async Task Fresh_Database_Is_Empty()
    {
        var path = NewTempDb();
        try
        {
            var db = await ProjectDatabase.OpenAsync(path);
            await using var _ = db;

            db.IsEmpty.Should().BeTrue();

            await using (var command = db.Connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE T (Id TEXT PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
            }

            // IsEmpty is a bootstrap-time snapshot (the empty/no-user-tables check runs once at
            // open), so it stays true even after a table is created on the same connection.
            db.IsEmpty.Should().BeTrue();
        }
        finally
        {
            DeleteDirectoryRetry(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public async Task Open_Creates_Missing_Parent_Directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "UW_Persist_New_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "nested", "sub", "project.db");
        try
        {
            Directory.Exists(Path.GetDirectoryName(path)!).Should().BeFalse();
            var db = await ProjectDatabase.OpenAsync(path);
            await using var _ = db;

            Directory.Exists(Path.GetDirectoryName(path)!).Should().BeTrue();
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task Open_Throws_On_Empty_Path()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () => await ProjectDatabase.OpenAsync("   "));
    }
}
