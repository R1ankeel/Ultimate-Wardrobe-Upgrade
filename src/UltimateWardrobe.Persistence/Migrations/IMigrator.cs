using Microsoft.Data.Sqlite;

namespace UltimateWardrobe.Persistence.Migrations;

/// <summary>
/// Applies pending <see cref="IMigration"/>s against an open <c>project.db</c> connection
/// (Phase 4 Sprint 4.1). Behaviour: reads <c>SchemaVersion</c>, applies only the missing versions
/// each in its own transaction, refuses to downgrade a schema newer than the app (fail-fast), and
/// copies the DB to <c>project.db.bak</c> before any upgrade of an existing schema.
/// </summary>
public interface IMigrator
{
    /// <summary>The registered migrations, ordered by <see cref="IMigration.Version"/> ascending.</summary>
    IReadOnlyList<IMigration> Migrations { get; }

    /// <summary>Reads the current schema version (0 when the DB has no <c>SchemaVersion</c> table).</summary>
    Task<int> GetCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken);

    /// <summary>
    /// Migrates <paramref name="connection"/> up to the newest registered version.
    /// <paramref name="databasePath"/> is the on-disk path used to produce <c>project.db.bak</c>
    /// before upgrading a schema that already exists.
    /// Throws <see cref="ProjectStoreException"/> for a DB newer than the app or a failed backup.
    /// </summary>
    Task<int> MigrateAsync(SqliteConnection connection, string databasePath, CancellationToken cancellationToken);
}
