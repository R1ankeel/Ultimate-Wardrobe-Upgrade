using FluentAssertions;
using UltimateWardrobe.Persistence;

namespace UltimateWardrobe.Tests.Persistence;

/// <summary>
/// Shared test infra for the Persistence suites: a temp directory + a disposed
/// <see cref="ProjectDatabase"/> with an associated <see cref="UnitOfWork"/>, plus robust
/// Windows-friendly temp-dir cleanup (SQLite WAL can briefly hold -shm/-wal handles after dispose).
/// </summary>
internal sealed class RepositoryTestDb : IAsyncDisposable
{
    private readonly string _rootDir;

    private RepositoryTestDb(string rootDir, ProjectDatabase db)
    {
        _rootDir = rootDir;
        Db = db;
        Uow = new UnitOfWork(db.Connection);
    }

    public string DbPath => Db.DbPath;

    public ProjectDatabase Db { get; }

    public UnitOfWork Uow { get; }

    /// <summary>Opens a fresh migrated <c>project.db</c> in a new temp directory.</summary>
    public static async Task<RepositoryTestDb> CreateAsync(string? prefix = null)
    {
        var rootDir = TestHelpers.NewTempDir(prefix ?? "UW_Repo_");
        var db = await ProjectDatabase.OpenAsync(Path.Combine(rootDir, "project.db"));
        return new RepositoryTestDb(rootDir, db);
    }

    public async ValueTask DisposeAsync()
    {
        await Uow.DisposeAsync();
        await Db.DisposeAsync();
        TestHelpers.DeleteDirectoryRetry(_rootDir);
    }
}

internal static class TestHelpers
{
    public static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void DeleteDirectoryRetry(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                Directory.Delete(directory, true);
                return;
            }
            catch (IOException) when (attempt < 39)
            {
                Thread.Sleep(50);
            }
        }
    }

    public static async Task<long> ScalarAsync(UnitOfWork uow, string sql)
    {
        await using var command = uow.Connection.CreateCommand();
        command.Transaction = uow.Transaction;
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
