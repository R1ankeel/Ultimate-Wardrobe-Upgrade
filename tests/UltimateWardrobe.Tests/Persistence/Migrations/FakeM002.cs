using Microsoft.Data.Sqlite;
using UltimateWardrobe.Persistence.Migrations;

namespace UltimateWardrobe.Tests.Persistence.Migrations;

/// <summary>
/// Test-only <c>M002</c>: bumps the schema to version 2 and adds a marker table, so the upgrade +
/// backup path can be exercised with <see cref="Migrator"/> without touching the shipped set.
/// </summary>
internal sealed class FakeM002 : IMigration
{
    public int Version => 2;

    public async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE FakeM002Marker (Id TEXT PRIMARY KEY);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
