using Microsoft.Data.Sqlite;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Persistence.Repositories;

/// <summary>
/// The cached <see cref="Catalog"/> per Overhaul (Phase 4 Sprint 4.2.5). The <c>CatalogCache</c>
/// table stores the whole catalog as one JSON column (<c>CatalogJson</c>) plus a <c>CachedAt</c>
/// timestamp; upsert is ON CONFLICT(<c>OverhaulId</c>) so re-saving a catalog replaces in place.
/// </summary>
public sealed class CatalogCacheRepository
{
    private readonly UnitOfWork _uow;

    public CatalogCacheRepository(UnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task UpsertAsync(Guid overhaulId, Catalog catalog, DateTime cachedAt, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO CatalogCache (OverhaulId, CatalogJson, CachedAt)
            VALUES ($overhaulId, $catalogJson, $cachedAt)
            ON CONFLICT(OverhaulId) DO UPDATE SET
              CatalogJson = excluded.CatalogJson,
              CachedAt = excluded.CachedAt;
            """;

        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$overhaulId", overhaulId.ToString());
        command.Parameters.AddWithValue("$catalogJson", PersistenceJson.Serialize(catalog));
        command.Parameters.AddWithValue("$cachedAt", RowCodecs.Utc(cachedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<(Catalog Catalog, DateTime CachedAt)?> GetAsync(Guid overhaulId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT CatalogJson, CachedAt FROM CatalogCache WHERE OverhaulId = $overhaulId;";

        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$overhaulId", overhaulId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var catalogJson = RowCodecs.Text(reader["CatalogJson"]);
        var catalog = PersistenceJson.Deserialize<Catalog>(catalogJson)
            ?? throw new ProjectStoreException($"Overhaul '{overhaulId}' has an unreadable CatalogJson.");
        return (catalog, RowCodecs.DateTime(RowCodecs.Text(reader["CachedAt"])));
    }

    public async Task DeleteAsync(Guid overhaulId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM CatalogCache WHERE OverhaulId = $overhaulId;";
        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$overhaulId", overhaulId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
