using Microsoft.Data.Sqlite;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Persistence.Repositories;

/// <summary>
/// CRUD for the <c>Overhaul</c> table (Phase 4 Sprint 4.2.2): upsert (stable by <c>Id</c>), get by
/// project or id, and delete. <c>SourceJson</c> holds the <see cref="CatalogSource"/> JSON
/// (amendment #2 - not flattened), <c>Policy</c> the <see cref="PatchPolicy"/> enum-name, and
/// <c>CreatedAt</c>/<c>ModifiedAt</c> the timestamps (Core 4.0.2 amendment).
/// </summary>
public sealed class OverhaulRepository
{
    private readonly UnitOfWork _uow;

    public OverhaulRepository(UnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task UpsertAsync(Overhaul overhaul, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO Overhaul (Id, ProjectId, Name, Policy, SourceJson, CreatedAt, ModifiedAt)
            VALUES ($id, $projectId, $name, $policy, $sourceJson, $created, $modified)
            ON CONFLICT(Id) DO UPDATE SET
              ProjectId = excluded.ProjectId,
              Name = excluded.Name,
              Policy = excluded.Policy,
              SourceJson = excluded.SourceJson,
              CreatedAt = excluded.CreatedAt,
              ModifiedAt = excluded.ModifiedAt;
            """;

        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", overhaul.Id.ToString());
        command.Parameters.AddWithValue("$projectId", overhaul.ProjectId.ToString());
        command.Parameters.AddWithValue("$name", overhaul.Name);
        command.Parameters.AddWithValue("$policy", RowCodecs.EnumName(overhaul.Policy));
        command.Parameters.AddWithValue("$sourceJson", PersistenceJson.Serialize<CatalogSource>(overhaul.Source));
        command.Parameters.AddWithValue("$created", RowCodecs.Utc(overhaul.CreatedAt));
        command.Parameters.AddWithValue("$modified", (object?)RowCodecs.UtcOrNull(overhaul.ModifiedAt) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Overhaul>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Id, ProjectId, Name, Policy, SourceJson, CreatedAt, ModifiedAt FROM Overhaul WHERE ProjectId = $projectId ORDER BY Name;";
        return await QueryRowsAsync(sql, "$projectId", projectId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Overhaul?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Id, ProjectId, Name, Policy, SourceJson, CreatedAt, ModifiedAt FROM Overhaul WHERE Id = $id;";
        var rows = await QueryRowsAsync(sql, "$id", id, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM Overhaul WHERE Id = $id;";
        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Overhaul>> QueryRowsAsync(string sql, string paramName, Guid paramValue, CancellationToken cancellationToken)
    {
        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(paramName, paramValue.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<Overhaul>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }
        return results;
    }

    private static Overhaul MapRow(SqliteDataReader reader)
    {
        var id = RowCodecs.Guid(reader["Id"]);
        var sourceJson = RowCodecs.Text(reader["SourceJson"]);
        var source = PersistenceJson.Deserialize<CatalogSource>(sourceJson)
            ?? throw new ProjectStoreException($"Overhaul '{id}' has an unreadable SourceJson: {sourceJson}.");

        return new Overhaul(
            id,
            RowCodecs.Text(reader["Name"]),
            RowCodecs.Guid(reader["ProjectId"]),
            source)
        {
            Policy = RowCodecs.ParseEnum<PatchPolicy>(reader["Policy"]),
            CreatedAt = RowCodecs.DateTime(RowCodecs.Text(reader["CreatedAt"])),
            ModifiedAt = RowCodecs.NullableDateTime(reader["ModifiedAt"]),
        };
    }
}
