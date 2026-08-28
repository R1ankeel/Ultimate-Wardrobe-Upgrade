using FluentAssertions;
using UltimateWardrobe.Persistence;

namespace UltimateWardrobe.Tests.Persistence;

/// <summary>
/// Sprint 4.3.4 - <see cref="ProjectStore.LoadAsync"/> failure paths surface as typed
/// <see cref="ProjectStoreException"/>s, never a crash: a missing database (a nonexistent file that
/// only just got migrated into an empty schema has no <c>Project</c> row), an unreadable/corrupt
/// file, and a database newer than this app (fail-fast refuse-to-downgrade).
/// </summary>
public class ProjectStoreLoadFailureTests
{
    private const string TempPrefix = "UW_Load_";

    [Fact]
    public async Task LoadAsync_MissingDatabase_Throws_Typed_Exception()
    {
        var root = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            var dbPath = Path.Combine(root, "project.db");

            var act = async () => await new ProjectStore(dbPath).LoadAsync(dbPath);

            (await act.Should().ThrowAsync<ProjectStoreException>())
                .WithMessage("*No Project found*");
        }
        finally
        {
            DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task LoadAsync_CorruptDatabase_Throws_Typed_Exception()
    {
        var root = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var dbPath = Path.Combine(root, "project.db");
            await File.WriteAllTextAsync(dbPath, "this is not a sqlite database");

            var act = async () => await new ProjectStore(dbPath).LoadAsync(dbPath);

            (await act.Should().ThrowAsync<ProjectStoreException>())
                .WithMessage("*project database*");
        }
        finally
        {
            DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task LoadAsync_NewerSchema_FailsFast()
    {
        var root = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            var dbPath = Path.Combine(root, "project.db");
            var db = await ProjectDatabase.OpenAsync(dbPath);
            await using (db)
            {
                var command = db.Connection.CreateCommand();
                command.CommandText = "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES (99, '01-01-2030');";
                await command.ExecuteNonQueryAsync();
            }

            var act = async () => await new ProjectStore(dbPath).LoadAsync(dbPath);

            (await act.Should().ThrowAsync<ProjectStoreException>())
                .WithMessage("*newer than this app*");
        }
        finally
        {
            DeleteDirectoryRetry(root);
        }
    }

    private static void DeleteDirectoryRetry(string? directory)
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
}
