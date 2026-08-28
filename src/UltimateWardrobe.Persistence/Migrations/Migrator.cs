using Microsoft.Data.Sqlite;

namespace UltimateWardrobe.Persistence.Migrations;

/// <summary>
/// Default <see cref="IMigrator"/> (Phase 4 Sprint 4.1): ordered, forward-only, transactional
/// migration apply plus <c>.bak</c> backup and an explicit refuse-to-downgrade guard.
/// Constructed with the full migration list so tests can inject extra migrations (e.g. a fake
/// <c>M002</c>) to exercise the upgrade + backup path without changing the shipped set.
/// </summary>
public sealed class Migrator : IMigrator
{
    private const string SchemaVersionTable = "SchemaVersion";

    private readonly IReadOnlyList<IMigration> _migrations;

    public Migrator(IEnumerable<IMigration> migrations)
    {
        _migrations = migrations.ToList();
    }

    /// <summary>The shipped migration set: <see cref="M001_Initial"/> only (schema version 1).</summary>
    public static Migrator CreateDefault() => new(new IMigration[] { new M001_Initial() });

    public IReadOnlyList<IMigration> Migrations => _migrations;

    public async Task<int> GetCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, SchemaVersionTable, cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task<int> MigrateAsync(SqliteConnection connection, string databasePath, CancellationToken cancellationToken)
    {
        var current = await GetCurrentVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        var highestKnown = _migrations.Count == 0 ? 0 : _migrations.Max(m => m.Version);

        if (current > highestKnown)
        {
            // Fail-fast: never downgrade a DB authored by a newer app/build.
            throw new ProjectStoreException(
                $"Project database schema version {current} is newer than this app supports (max {highestKnown}); refusing to downgrade.");
        }

        var pending = _migrations.Where(m => m.Version > current).OrderBy(m => m.Version).ToList();
        if (pending.Count == 0)
        {
            return current;
        }

        // Back up an existing schema before changing it (roadmap section 6.5 task 4.6). A fresh DB
        // (version 0, no prior schema) has nothing to preserve, so no .bak is produced.
        if (current > 0)
        {
            await BackUpAsync(databasePath, cancellationToken).ConfigureAwait(false);
        }

        foreach (var migration in pending)
        {
            await ApplyInTransactionAsync(connection, migration, cancellationToken).ConfigureAwait(false);
        }

        return pending.Max(m => m.Version);
    }

    private static async Task ApplyInTransactionAsync(SqliteConnection connection, IMigration migration, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await migration.ApplyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES ($version, $appliedAt);";
                insert.Parameters.AddWithValue("$version", migration.Version);
                insert.Parameters.AddWithValue("$appliedAt", DateTime.UtcNow.ToString("O"));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A failed migration rolls back entirely: no half-built schema, no SchemaVersion bump.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task BackUpAsync(string databasePath, CancellationToken cancellationToken)
    {
        var backupPath = databasePath + ".bak";
        try
        {
            await Task.CompletedTask;
            File.Copy(databasePath, backupPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProjectStoreException($"Unable to back up '{databasePath}' to '{backupPath}' before migrating.", ex);
        }
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string name, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", name);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result) > 0;
    }
}
