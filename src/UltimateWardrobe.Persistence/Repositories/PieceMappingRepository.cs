using Microsoft.Data.Sqlite;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Persistence.Repositories;

/// <summary>
/// CRUD for the <c>PieceMapping</c> table (Phase 4 Sprint 4.2.4). Upsert is ON CONFLICT on the
/// <c>UNIQUE(OverhaulId, TargetPieceEditorId, TargetGender)</c> constraint - the DB-level mirror of
/// <see cref="PieceMapping.UniqueKey"/> - so a second assign for the same key REPLACES the existing
/// row instead of duplicating it (mirrors <c>MappingService.AssignDonor</c>). Deletes are
/// by row <c>Id</c> so callers can enforce leaves-first ordering.
/// </summary>
public sealed class PieceMappingRepository
{
    private readonly UnitOfWork _uow;

    public PieceMappingRepository(UnitOfWork uow)
    {
        _uow = uow;
    }

    private const string Columns = "Id, OverhaulId, TargetArmorSetId, TargetPieceEditorId, TargetGender, DonorAssetId, DonorPieceEditorId, DonorMeshPath, BodyConversionPatchAssetId, PhysicsPatchAssetId, Status, Notes";

    public async Task UpsertAsync(PieceMapping mapping, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO PieceMapping (Id, OverhaulId, TargetArmorSetId, TargetPieceEditorId, TargetGender, DonorAssetId, DonorPieceEditorId, DonorMeshPath, BodyConversionPatchAssetId, PhysicsPatchAssetId, Status, Notes)
            VALUES ($id, $overhaulId, $armorSetId, $pieceEditorId, $gender, $donorId, $donorPiece, $donorMesh, $bodyPatch, $physicsPatch, $status, $notes)
            ON CONFLICT(OverhaulId, TargetPieceEditorId, TargetGender) DO UPDATE SET
              Id = excluded.Id,
              TargetArmorSetId = excluded.TargetArmorSetId,
              DonorAssetId = excluded.DonorAssetId,
              DonorPieceEditorId = excluded.DonorPieceEditorId,
              DonorMeshPath = excluded.DonorMeshPath,
              BodyConversionPatchAssetId = excluded.BodyConversionPatchAssetId,
              PhysicsPatchAssetId = excluded.PhysicsPatchAssetId,
              Status = excluded.Status,
              Notes = excluded.Notes;
            """;

        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", mapping.Id.ToString());
        command.Parameters.AddWithValue("$overhaulId", mapping.OverhaulId.ToString());
        command.Parameters.AddWithValue("$armorSetId", mapping.TargetArmorSetId);
        command.Parameters.AddWithValue("$pieceEditorId", mapping.TargetPieceEditorId);
        command.Parameters.AddWithValue("$gender", RowCodecs.EnumName(mapping.TargetGender));
        command.Parameters.AddWithValue("$donorId", mapping.DonorAssetId.ToString());
        command.Parameters.AddWithValue("$donorPiece", mapping.DonorPieceEditorId);
        command.Parameters.AddWithValue("$donorMesh", mapping.DonorMeshPath);
        command.Parameters.AddWithValue("$bodyPatch", (object?)RowCodecs.NullableGuidToString(mapping.BodyConversionPatchAssetId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$physicsPatch", (object?)RowCodecs.NullableGuidToString(mapping.PhysicsPatchAssetId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", RowCodecs.EnumName(mapping.Status));
        command.Parameters.AddWithValue("$notes", (object?)mapping.Notes ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PieceMapping>> GetByOverhaulAsync(Guid overhaulId, CancellationToken cancellationToken)
    {
        const string sql = $"SELECT {Columns} FROM PieceMapping WHERE OverhaulId = $overhaulId ORDER BY TargetPieceEditorId;";

        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$overhaulId", overhaulId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<PieceMapping>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }
        return results;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM PieceMapping WHERE Id = $id;";
        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static PieceMapping MapRow(SqliteDataReader reader)
    {
        return new PieceMapping(
            RowCodecs.Guid(reader["Id"]),
            RowCodecs.Guid(reader["OverhaulId"]),
            RowCodecs.Text(reader["TargetArmorSetId"]),
            RowCodecs.Text(reader["TargetPieceEditorId"]),
            RowCodecs.ParseEnum<Gender>(reader["TargetGender"]),
            RowCodecs.Guid(reader["DonorAssetId"]),
            RowCodecs.Text(reader["DonorPieceEditorId"]),
            RowCodecs.Text(reader["DonorMeshPath"]),
            RowCodecs.NullableGuid(reader["BodyConversionPatchAssetId"]),
            RowCodecs.NullableGuid(reader["PhysicsPatchAssetId"]),
            RowCodecs.ParseEnum<MappingStatus>(reader["Status"]),
            RowCodecs.NullableText(reader["Notes"]));
    }
}
