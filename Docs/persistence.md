# SQLite Persistence

> Phase 4, Sprints 4.0-4.4 done - `src/UltimateWardrobe.Persistence` - one `project.db` per Project: a versioned SQLite schema, forward-only migrations with `.bak` backup, rename-free row codecs, five repositories over a single-connection `UnitOfWork`, and the `IProjectStore.SaveAsync`/`LoadAsync` facade that persists and rebuilds the whole in-memory graph. `Persistence` depends on `Core` only (plus `Microsoft.Data.Sqlite`); the JSON conversion of `Catalog`/`CatalogSource`/`DonorAsset` is implemented locally and mirrors the Phase 1 `CatalogCacheStore` conventions.

## Overview

Every user action in the Phase 6 UI will be saved immediately ("auto-save" via the repositories, one transaction each); `SaveAsync(Project)` is the coarse whole-graph facade for on-demand/close saves. A project can be paused mid-way through a 200-set mapping and resumed next week - the reloaded graph feeds `MappingService.GetArmorSetStatus`/`GetOverhaulProgress` with identical statuses/progress.

The DB the project is bound to: `IProjectStore.SaveAsync(Project)` is constructed bound to a path; `IProjectStore.LoadAsync(path)` opens any file. `ProjectDatabase` opens/creates + migrates the file; `UnitOfWork` owns the transaction lifetime on its single long-lived connection; repositories are thin `Microsoft.Data.Sqlite` wrappers binding commands to the ambient transaction; `ProjectStore` routes both the whole-graph save and the load through them.

## Components

| File | Purpose |
|------|---------|
| `ProjectDatabase.cs:1` | Opens/creates one `project.db` (creates missing parent dirs), applies `PRAGMA journal_mode=WAL` + `PRAGMA foreign_keys=ON` on every connection, snapshots empty-DB state, then runs the migrations. `Pooling=False` so dispose really closes the file. Owns the connection (`IAsyncDisposable`). |
| `UnitOfWork.cs:1` | Transaction boundary over the single connection. `BeginAsync` issues `PRAGMA defer_foreign_keys=ON` inside EVERY new transaction (it is a transaction-level pragma that SQLite resets to OFF after commit/rollback); `CommitAsync`/`RollbackAsync`/`DisposeAsync`. Never closes the connection. |
| `RowCodecs.cs:1` | Thin value codecs: ISO-8601 `DateTime` ("O" format, round-trips ticks), `Guid` text, enum-as-name strings, nullable variants. |
| `PersistenceJson.cs:1` | Shared `System.Text.Json` options - camelCase + `JsonStringEnumConverter` + `WhenWritingNull`, matching the Phase 1 `CatalogCacheStore` conventions. |
| `CatalogSourceJsonConverter.cs:1` | Custom converter for the abstract `CatalogSource`: a `kind` discriminator - `"vanilla"` (`rootPath` + `pluginNames`) or `"story"` (`rootPath` + `mainPlugin` + `masters`). |
| `ProjectStore.cs:1` | The `IProjectStore` facade: `SaveAsync` (whole-graph, one transaction, upsert-only, rollback on failure) and `LoadAsync` (open + migrate + rebuild the graph). |
| `ProjectStoreException.cs:1` | Typed persistence exception - a missing/empty/corrupt DB or a newer schema surfaces as this, never a crash. |
| `Repositories/ProjectRepository.cs:1` | Upsert by `Id` (stable domain id), `GetAllAsync` (the load-side "exactly one Project" reader). |
| `Repositories/OverhaulRepository.cs:1` | Upsert by `Id` incl. `SourceJson` (the whole `CatalogSource`), `Policy`, `CreatedAt`/`ModifiedAt`; get by project/id; delete. |
| `Repositories/DonorAssetRepository.cs:1` | Upsert by `ImportId` (the DB PK) with JSON columns + `Kind` + `ImportedAt`; explicit `projectId` parameter (the domain carries none); get by project/id; delete. |
| `Repositories/PieceMappingRepository.cs:1` | Upsert ON CONFLICT on `UNIQUE(OverhaulId, TargetPieceEditorId, TargetGender)` - the DB mirror of `PieceMapping.UniqueKey`, so a second assign REPLACES instead of duplicating; get by overhaul; delete by row `Id`. |
| `Repositories/CatalogCacheRepository.cs:1` | Per-Overhaul `Catalog` cache: whole-`Catalog` JSON + `CachedAt`, upsert/get/delete. |
| `Migrations/IMigration.cs:1` | `Version` + `ApplyAsync(connection, transaction, ct)`. |
| `Migrations/IMigrator.cs:1` | `GetCurrentVersionAsync` / `MigrateAsync`. |
| `Migrations/Migrator.cs:1` | Ordered, forward-only, transactional apply; `.bak` backup before upgrading an existing schema; refuse-to-downgrade fail-fast. |
| `Migrations/M001_Initial.cs:1` | Schema version 1 - the full section 4.2 DDL. |

## The `project.db` schema

One row per domain root, children keyed by stable domain `Guid`s (stored as text):

```sql
CREATE TABLE SchemaVersion (Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL);

CREATE TABLE Project (
  Id TEXT PRIMARY KEY, Name TEXT NOT NULL, RootPath TEXT NOT NULL,
  SchemaVersion INTEGER NOT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL
);

CREATE TABLE Overhaul (
  Id TEXT PRIMARY KEY, ProjectId TEXT NOT NULL REFERENCES Project(Id),
  Name TEXT NOT NULL, Policy TEXT NOT NULL DEFAULT 'Loose', SourceJson TEXT NOT NULL,
  CreatedAt TEXT NOT NULL, ModifiedAt TEXT
);

CREATE TABLE DonorAsset (
  ImportId TEXT PRIMARY KEY, ProjectId TEXT NOT NULL REFERENCES Project(Id),
  OriginalFileName TEXT NOT NULL, ArchiveHash TEXT NOT NULL, ExtractedPath TEXT NOT NULL,
  Kind TEXT NOT NULL, ImportedAt TEXT NOT NULL,
  FileManifestJson TEXT NOT NULL, ProvidedSetsJson TEXT NOT NULL,
  DetectedBodySlideJson TEXT NOT NULL, DetectedPhysicsJson TEXT NOT NULL
);

CREATE TABLE PieceMapping (
  Id TEXT PRIMARY KEY, OverhaulId TEXT NOT NULL REFERENCES Overhaul(Id),
  TargetArmorSetId TEXT NOT NULL, TargetPieceEditorId TEXT NOT NULL,
  TargetGender TEXT NOT NULL, DonorAssetId TEXT NOT NULL REFERENCES DonorAsset(ImportId),
  DonorPieceEditorId TEXT NOT NULL, DonorMeshPath TEXT NOT NULL,
  BodyConversionPatchAssetId TEXT REFERENCES DonorAsset(ImportId),
  PhysicsPatchAssetId TEXT REFERENCES DonorAsset(ImportId),
  Status TEXT NOT NULL, Notes TEXT,
  UNIQUE(OverhaulId, TargetPieceEditorId, TargetGender)      -- == PieceMapping.UniqueKey
);

CREATE TABLE CatalogCache (
  OverhaulId TEXT PRIMARY KEY REFERENCES Overhaul(Id),
  CatalogJson TEXT NOT NULL, CachedAt TEXT NOT NULL
);

CREATE INDEX IX_PieceMapping_Overhaul ON PieceMapping(OverhaulId);
CREATE INDEX IX_DonorAsset_Project ON DonorAsset(ProjectId);
```

Notes:
- `foreign_keys=ON` is a connection-level pragma without which the `REFERENCES` clauses are not enforced (mission-critical - a repository FK test proves a mapping to a donor not in the DB is rejected ONLY because it is set). SQLite does not enable it by default.
- The rich collections (`FileManifest`, `ProvidedSets`, `Detected*`) and the whole `Catalog` cache round-trip as camelCase JSON columns (see Serialization below).
- `DonorAsset.ExtractedPath` is the authoritative on-disk pointer after load; the extracted files are NOT re-verified or re-copied on load (Phase 4 scope amendment #3).

## Migration engine + backup (Sprint 4.1)

`ProjectDatabase.OpenAsync` snapshots emptiness BEFORE migration (a brand-new DB reads as empty, the bootstrap signal), then runs `Migrator.CreateDefault()`:
- Fresh DB (no user tables) -> `M001_Initial` builds the whole schema.
- Existing DB -> only the missing versions, each inside its own transaction; a failed migration rolls back the DDL and leaves `SchemaVersion` untouched.
- DB newer than the app -> fail-fast `ProjectStoreException` (forward-only, refuse-to-downgrade).
- Before upgrading an EXISTING schema (`current > 0`) the migrator copies `project.db` to `project.db.bak`; a failed backup aborts without touching the schema. A fresh DB has nothing to preserve, so no `.bak`.

## Repositories + UnitOfWork + IProjectStore (Sprints 4.2-4.4)

- `UnitOfWork` wraps one long-lived `SqliteConnection`. `BeginAsync` keeps FK checks deferred for the whole transaction (`PRAGMA defer_foreign_keys=ON` re-issued on every begin - transaction-level pragma). Repositories bind each command's `Transaction` to `_uow.Transaction`; when none is active the connection auto-commits.
- `SaveAsync(Project)` persists the whole graph in ONE transaction: Project row -> `Library.Assets` -> each Overhaul + its Mappings + its `Catalog` cache. Save is upsert-only by stable domain id (issue 3 - no delete-then-reinsert, so a referenced `DonorAsset` cannot vanish mid-save). On ANY failure the transaction rolls back, leaving the DB byte-identical.
- `LoadAsync(path)` opens + migrates, reads the single Project row, its assets, each Overhaul (with catalog cache + mappings), and REBUILDS the graph. An `Overhaul` is reconstructed (not re-used from the repository read) because `Overhaul.Catalog` is `init`-only and must be set at construction; `Project.Library` and `Overhaul.Mappings` are filled in the exact shape `MappingService` expects.
- A DB with no Project row (a missing file that just got migrated to emptiness), an unreadable file, or a newer schema all surface as a typed `ProjectStoreException`.
- Auto-save path: the Phase 6 UI calls the repositories directly (one transaction per user action); the row codecs are shared with `SaveAsync`, so both paths cannot drift.

## JSON serialization (Sprint 4.0)

`PersistenceJson` mirrors the Phase 1 `CatalogCacheStore` conventions exactly: camelCase + `JsonStringEnumConverter` + `WhenWritingNull` + compact. `Persistence` ships its own `CatalogSourceJsonConverter` (the `kind` discriminator) so it never depends on `Scanner` (dependency rule). `DonorAsset`, `DonorProvidedSet`, `Variant`, `Piece`, `DonorFileEntry`, `ArmorSet` and `Catalog` all round-trip with the built-in ctor/`init` binding. Enums like `PatchPolicy`, `DonorAssetKind`, `Gender`, `MappingStatus` are stored as name strings (the `EnumName`/`ParseEnum` row codecs / enum-string converter), and timestamps as ISO-8601 "O" so they round-trip without drift.

## Test strategy (Sprint 4.4)

- `Category!=Integration` is fully self-contained: temp-file SQLite per test (`UW_Repo_`/`UW_Store_`/`UW_Load_` prefixes under `%TEMP%`), runtime-built domain objects, no `ModsForTests`, no game files. Cleanup retries on Windows (WAL can briefly hold `-shm`/`-wal` handles) and asserts 0 artifacts after.
- `RepositoryTestDb` shared infra: temp dir + disposed `ProjectDatabase` + `UnitOfWork`.
- Suites: `ProjectDatabaseTests` (open/create + WAL + `foreign_keys` + empty detection), `SerializationRoundTripTests` (JSON identity), `MigrationTests` (fresh/idempotent/newer/upgrade+backup/corrupt/failed-backup), `RepositoryCrudTests` + `RepositoryMappingAndTransactionTests` (CRUD, upsert-replace, FK enforcement, leaves-first delete, rollback/commit, and the pragma-re-issue-on-every-begin proof), `ProjectStoreRoundTripTests` (deep-equality round-trip + `MappingService` status/progress parity + double-save upsert-only idempotency), `ProjectStoreLoadFailureTests` (typed exceptions).
- `Category=Integration` (`ProjectStoreIntegrationTests`): the full end-to-end loop over a REAL donor (Red Hood - HIMBO, the esp-less branch-2 fixture: `BodyConversionPatch`, real BodySlide + physics flags) - import + classify -> synthesize the Iron catalog -> assign synthetic donors + ATTACH the real donor as the body patch -> `SaveAsync` -> reopen in a fresh `ProjectDatabase` -> `LoadAsync` -> assert the reloaded graph deep-equals, statuses/progress are identical, and the extracted donor folder path still resolves. Auto-skips with an output note when `ModsForTests/Armor` lacks the fixture; cleans `%TEMP%/UW_Donor_*` + the test DB dir in `finally`.

## Status

- Sprint 4.0 - project scaffolding + `ProjectDatabase` bootstrap + JSON layer + Core amendment (`Overhaul.CreatedAt`/`ModifiedAt`): done, 509 tests green.
- Sprint 4.1 - migration engine + `M001_Initial` + `.bak` backup + fail-fast: done, 515 tests green.
- Sprint 4.2 - `UnitOfWork` + five repositories + transaction/rollback/FK tests: done, 528 tests green.
- Sprint 4.3 - `IProjectStore.SaveAsync`/`LoadAsync` + round-trip identity + corrupt/newer-schema typed failures + upsert-only idempotency: done, 535 tests green.
- Sprint 4.4 - Integration round-trip spot-check + docs close-out: done, full suite 536 tests green (521 non-integration + 15 Integration), Release 0 warnings / 0 errors, no artifacts.

Next: Phase 5 (patcher) consumes the persisted `Project` graph.