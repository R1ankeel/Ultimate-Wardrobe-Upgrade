using Microsoft.Data.Sqlite;

namespace UltimateWardrobe.Persistence.Migrations;

/// <summary>
/// <c>M001_Initial</c> - creates the section 4.2 <c>project.db</c> schema (Phase 4 Sprint 4.1).
/// Source of truth is <c>Plans/phase4.md</c> section 4.2 (adapted from roadmap section 6.2):
/// <c>SchemaVersion</c> + <c>Project</c>/<c>Overhaul</c>/<c>DonorAsset</c>/<c>PieceMapping</c>/
/// <c>CatalogCache</c> with the FKs and the <c>UNIQUE(OverhaulId, TargetPieceEditorId, TargetGender)</c>
/// that mirrors <c>PieceMapping.UniqueKey</c>. The <see cref="Migrator"/> inserts the
/// <c>SchemaVersion</c> row after this DDL runs.
/// </summary>
public sealed class M001_Initial : IMigration
{
    public int Version => 1;

    public async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SchemaM001;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SchemaM001 = """
        CREATE TABLE SchemaVersion (Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL);

        CREATE TABLE Project (
          Id TEXT PRIMARY KEY,
          Name TEXT NOT NULL,
          RootPath TEXT NOT NULL,
          SchemaVersion INTEGER NOT NULL,
          CreatedAt TEXT NOT NULL,
          ModifiedAt TEXT NOT NULL
        );

        CREATE TABLE Overhaul (
          Id TEXT PRIMARY KEY,
          ProjectId TEXT NOT NULL REFERENCES Project(Id),
          Name TEXT NOT NULL,
          Policy TEXT NOT NULL DEFAULT 'Loose',
          SourceJson TEXT NOT NULL,
          CreatedAt TEXT NOT NULL,
          ModifiedAt TEXT
        );

        CREATE TABLE DonorAsset (
          ImportId TEXT PRIMARY KEY,
          ProjectId TEXT NOT NULL REFERENCES Project(Id),
          OriginalFileName TEXT NOT NULL,
          ArchiveHash TEXT NOT NULL,
          ExtractedPath TEXT NOT NULL,
          Kind TEXT NOT NULL,
          ImportedAt TEXT NOT NULL,
          FileManifestJson TEXT NOT NULL,
          ProvidedSetsJson TEXT NOT NULL,
          DetectedBodySlideJson TEXT NOT NULL,
          DetectedPhysicsJson TEXT NOT NULL
        );

        CREATE TABLE PieceMapping (
          Id TEXT PRIMARY KEY,
          OverhaulId TEXT NOT NULL REFERENCES Overhaul(Id),
          TargetArmorSetId TEXT NOT NULL,
          TargetPieceEditorId TEXT NOT NULL,
          TargetGender TEXT NOT NULL,
          DonorAssetId TEXT NOT NULL REFERENCES DonorAsset(ImportId),
          DonorPieceEditorId TEXT NOT NULL,
          DonorMeshPath TEXT NOT NULL,
          BodyConversionPatchAssetId TEXT REFERENCES DonorAsset(ImportId),
          PhysicsPatchAssetId TEXT REFERENCES DonorAsset(ImportId),
          Status TEXT NOT NULL,
          Notes TEXT,
          UNIQUE(OverhaulId, TargetPieceEditorId, TargetGender)
        );

        CREATE TABLE CatalogCache (
          OverhaulId TEXT PRIMARY KEY REFERENCES Overhaul(Id),
          CatalogJson TEXT NOT NULL,
          CachedAt TEXT NOT NULL
        );

        CREATE INDEX IX_PieceMapping_Overhaul ON PieceMapping(OverhaulId);
        CREATE INDEX IX_DonorAsset_Project ON DonorAsset(ProjectId);
        """;
}
