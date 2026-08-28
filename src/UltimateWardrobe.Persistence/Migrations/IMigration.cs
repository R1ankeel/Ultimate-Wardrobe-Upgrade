using Microsoft.Data.Sqlite;

namespace UltimateWardrobe.Persistence.Migrations;

/// <summary>
/// A single forward-only schema migration (Phase 4 Sprint 4.1). Each migration bumps the schema
/// <see cref="Version"/> and is applied inside its own transaction by <see cref="Migrator"/>.
/// Migrations must be immutable and deterministic: the same version always produces the same
/// schema, and once shipped a migration must never be edited (new versions only add M00x).
/// </summary>
public interface IMigration
{
    /// <summary>The schema version this migration produces (1 = <c>M001_Initial</c>).</summary>
    int Version { get; }

    /// <summary>
    /// Applies this migration's DDL/DML on the given connection. Runs inside a transaction begun
    /// by the caller, so every <see cref="SqliteCommand"/> must have its
    /// <see cref="SqliteCommand.Transaction"/> set to <paramref name="transaction"/> or SQLite will
    /// reject it while a local transaction is pending.
    /// </summary>
    Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken);
}
