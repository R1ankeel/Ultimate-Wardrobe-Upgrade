using Microsoft.Data.Sqlite;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Persistence.Repositories;

/// <summary>
/// CRUD for the <c>DonorAsset</c> table (Phase 4 Sprint 4.2.3): upsert (stable by
/// <c>ImportId</c>), get by project/id, and delete. The rich collections round-trip as JSON columns
/// (<c>FileManifestJson</c>, <c>ProvidedSetsJson</c>, <c>DetectedBodySlideJson</c>,
/// <c>DetectedPhysicsJson</c>), <c>Kind</c> as the <see cref="DonorAssetKind"/> enum-name, and
/// <c>ImportedAt</c> as an ISO-8601 timestamp.
/// </summary>
public sealed class DonorAssetRepository
{
    private readonly UnitOfWork _uow;

    public DonorAssetRepository(UnitOfWork uow)
    {
        _uow = uow;
    }

    private const string Columns = "ImportId, ProjectId, OriginalFileName, ArchiveHash, ExtractedPath, Kind, ImportedAt, FileManifestJson, ProvidedSetsJson, DetectedBodySlideJson, DetectedPhysicsJson";

    public async Task UpsertAsync(DonorAsset asset, Guid projectId, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO DonorAsset (ImportId, ProjectId, OriginalFileName, ArchiveHash, ExtractedPath, Kind, ImportedAt, FileManifestJson, ProvidedSetsJson, DetectedBodySlideJson, DetectedPhysicsJson)
            VALUES ($importId, $projectId, $fileName, $archiveHash, $extractedPath, $kind, $importedAt, $manifestJson, $providedJson, $bodySlideJson, $physicsJson)
            ON CONFLICT(ImportId) DO UPDATE SET
              ProjectId = excluded.ProjectId,
              OriginalFileName = excluded.OriginalFileName,
              ArchiveHash = excluded.ArchiveHash,
              ExtractedPath = excluded.ExtractedPath,
              Kind = excluded.Kind,
              ImportedAt = excluded.ImportedAt,
              FileManifestJson = excluded.FileManifestJson,
              ProvidedSetsJson = excluded.ProvidedSetsJson,
              DetectedBodySlideJson = excluded.DetectedBodySlideJson,
              DetectedPhysicsJson = excluded.DetectedPhysicsJson;
            """;

        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$importId", asset.ImportId.ToString());
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$fileName", asset.OriginalFileName);
        command.Parameters.AddWithValue("$archiveHash", asset.ArchiveHash);
        command.Parameters.AddWithValue("$extractedPath", asset.ExtractedPath);
        command.Parameters.AddWithValue("$kind", RowCodecs.EnumName(asset.Kind));
        command.Parameters.AddWithValue("$importedAt", RowCodecs.Utc(asset.ImportedAt));
        command.Parameters.AddWithValue("$manifestJson", PersistenceJson.Serialize(asset.FileManifest));
        command.Parameters.AddWithValue("$providedJson", PersistenceJson.Serialize(asset.ProvidedSets));
        command.Parameters.AddWithValue("$bodySlideJson", PersistenceJson.Serialize(asset.DetectedBodySlideFiles));
        command.Parameters.AddWithValue("$physicsJson", PersistenceJson.Serialize(asset.DetectedPhysicsFiles));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DonorAsset>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        const string sql = $"SELECT {Columns} FROM DonorAsset WHERE ProjectId = $projectId ORDER BY ImportId;";
        return await QueryRowsAsync(sql, "$projectId", projectId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DonorAsset?> GetAsync(Guid importId, CancellationToken cancellationToken)
    {
        const string sql = $"SELECT {Columns} FROM DonorAsset WHERE ImportId = $id;";
        var rows = await QueryRowsAsync(sql, "$id", importId, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task DeleteAsync(Guid importId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM DonorAsset WHERE ImportId = $id;";
        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", importId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DonorAsset>> QueryRowsAsync(string sql, string paramName, Guid paramValue, CancellationToken cancellationToken)
    {
        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(paramName, paramValue.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<DonorAsset>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }
        return results;
    }

    private static DonorAsset MapRow(SqliteDataReader reader)
    {
        var importId = RowCodecs.Guid(reader["ImportId"]);
        var manifestJson = RowCodecs.Text(reader["FileManifestJson"]);
        var providedJson = RowCodecs.Text(reader["ProvidedSetsJson"]);
        var bodySlideJson = RowCodecs.Text(reader["DetectedBodySlideJson"]);
        var physicsJson = RowCodecs.Text(reader["DetectedPhysicsJson"]);

        return new DonorAsset(
            importId,
            RowCodecs.Text(reader["OriginalFileName"]),
            RowCodecs.Text(reader["ExtractedPath"]),
            RowCodecs.DateTime(RowCodecs.Text(reader["ImportedAt"])),
            RowCodecs.Text(reader["ArchiveHash"]),
            RowCodecs.ParseEnum<DonorAssetKind>(reader["Kind"]),
            PersistenceJson.Deserialize<IReadOnlyList<DonorProvidedSet>>(providedJson),
            PersistenceJson.Deserialize<IReadOnlyList<DonorFileEntry>>(manifestJson),
            PersistenceJson.Deserialize<IReadOnlyList<string>>(bodySlideJson),
            PersistenceJson.Deserialize<IReadOnlyList<string>>(physicsJson));
    }
}
