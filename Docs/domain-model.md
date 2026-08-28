# Domain Model

> Phase 0.1 - `src/UltimateWardrobe.Core` - POCOs, enums, invariants, abstractions. No I/O, no external dependencies.

## Package

- `UltimateWardrobe.Core` targets `net10.0-windows`, `LangVersion 13`, `Nullable enable`
- Zero NuGet dependencies - BCL only
- All domain types are validated in constructors; invalid state throws `ArgumentException` / `ArgumentNullException`

## Enums

All enums have `Unknown = 0` as safe fallback for string deserialization.

| Enum | Values | Notes |
|------|--------|-------|
| `Gender` | `Unknown, Male, Female, Unisex` | Per-variant |
| `WeightClass` | `Unknown, Light, Heavy, Clothing, Any` | From keywords |
| `ArmorSetStatus` | `Unknown, NotStarted, InProgress, Mapped, NeedsPatch, Done` | Computed from PieceMapping in Phase 4 |
| `MappingStatus` | `Unknown, Pending, Mapped, NeedsPatch, Done` | Per PieceMapping |
| `CatalogSourceKind` | `Unknown, VanillaPlusDlc, StoryMod` | Discriminator for CatalogSource |
| `DonorAssetKind` | `Unknown, FullReplacer, BodyConversionPatch, PhysicsPatch` | Auto-detected from content, overridable in UI |
| `BodyType` | `Unknown, Vanilla, CBBE, ThreeBA, BHUNP, HIMBO` | Extensible - stored as string + enum |
| `PhysicsType` | `Unknown, None, HDT_SMP, CBPC, SMP_3BA` | Extensible |
| `ArchiveFormat` | `Unknown, SevenZip, Zip, Rar` | Detected by magic bytes |
| `PatchPolicy` | `Loose, RequireBodyConversion, RequirePhysics, RequireBoth` | Overhaul target-body / physics demand (Sprint 3.0.2) |

String round-trip via `Enum.Parse` / `ToString` is covered by tests at `tests/UltimateWardrobe.Tests/Core/EnumTests.cs:1`.

## Domain Types

### Project

```csharp
Project(Guid id, string name, string rootPath, int schemaVersion = 1)
  Id, Name, RootPath, Library (1:1), Overhauls, CreatedAt, ModifiedAt, SchemaVersion
```

- `Library` is created inside the constructor - `Project` owns exactly one `DonorLibrary` with matching `ProjectId`
- Guards: empty `id`, empty `name`/`rootPath` throw

Source: `src/UltimateWardrobe.Core/Domain/Project.cs:1`

### Overhaul

```csharp
Overhaul(Guid id, string name, Guid projectId, CatalogSource source)
  Id, Name, ProjectId, Source (init-only), Mappings
  PatchPolicy Policy (init-only, default Loose)   [Phase 3.0.2 amendment]
  Catalog? Catalog (init-only)                    [Phase 4.3 amendment]
```

- `Source` is immutable after construction - changing source requires a new `Overhaul`
- Guards: empty ids / name / null source
- `Policy` (Sprint 3.0.2) is the machine-readable target-body / physics demand for the Overhaul
  (roadmap 5.3); defaults to `PatchPolicy.Loose`. Additive - existing construction is unaffected.
  See `src/UltimateWardrobe.Core/Enums/PatchPolicy.cs`.
- `Catalog` (Sprint 4.3) is the scanned source catalog this overhaul maps over - the domain side of the
  per-overhaul `CatalogCache` row; `null` until a scan attaches it. Additive `init`, so an `Overhaul`
  is reconstructed (not reused) on `LoadAsync` to set it at construction.

Source: `src/UltimateWardrobe.Core/Domain/Overhaul.cs:1`

### CatalogSource hierarchy

```csharp
abstract CatalogSource(string rootPath, CatalogSourceKind kind)
VanillaCatalogSource(string rootPath, IReadOnlyList<string>? pluginNames)
  : CatalogSource(rootPath, VanillaPlusDlc)
StoryModCatalogSource(string rootPath, string mainPlugin, IReadOnlyList<string>? masters)
  : CatalogSource(rootPath, StoryMod)
```

Source: `src/UltimateWardrobe.Core/Domain/CatalogSource.cs:1`

Tests: `tests/UltimateWardrobe.Tests/Core/CatalogSourceTests.cs:1` - polymorphism and validation.

### ArmorSet / Variant / Piece / Catalog

```csharp
Piece(string editorId, uint formId, string slot, string? armaEditorId, string? meshPath, IReadOnlyList<string>? texturePaths)
Variant(Gender gender, WeightClass weight, IReadOnlyList<Piece> pieces)
ArmorSet(string id, string displayName, IReadOnlyList<Variant> variants, ArmorSetStatus status = NotStarted)
Catalog(CatalogSource source, IReadOnlyList<ArmorSet> sets, ScanStats? stats, IReadOnlyList<ScanWarning>? warnings)
ScanStats { TotalArmo, TotalArma, GroupedSets, Skipped, MissingFiles }
ScanWarning(string message, string? editorId)
```

- `Piece` validates `editorId` + `slot`; `FormId` is `uint` (0 = none) to avoid Mutagen dependency in Core
- `Variant` owns `Gender + WeightClass` and its `Pieces`
- `ArmorSet.Status` is stored but will be computed from `PieceMapping` in Phase 4

Sources: `src/UltimateWardrobe.Core/Domain/Piece.cs:1`, `Variant.cs:1`, `ArmorSet.cs:1`, `Catalog.cs:1`

### DonorLibrary / DonorAsset

```csharp
DonorLibrary(Guid projectId) { ProjectId, Assets }
DonorProvidedSet(string id, string displayName, IReadOnlyList<Variant>? variants = null)
  { Id, DisplayName, Variants }   // Variants added in Sprint 2.0.2 (Scope amendment #2)
DonorFileEntry(string relativePath, long length)
  { RelativePath, Length }        // manifest entry, slash-normalized (Sprint 2.0.2, amendment #1)
DonorAsset(Guid importId, string originalFileName, string extractedPath, DateTime importedAt, string archiveHash, DonorAssetKind kind = FullReplacer, IReadOnlyList<DonorProvidedSet>? providedSets = null, IReadOnlyList<DonorFileEntry>? fileManifest = null, IReadOnlyList<string>? detectedBodySlideFiles = null, IReadOnlyList<string>? detectedPhysicsFiles = null)
```

- `DonorProvidedSet.Variants` reuses the catalog `Variant/Piece` shapes, so a donor-provided set is directly comparable with a catalog `ArmorSet` (roadmap 4.1); the 2-arg `(id, displayName)` ctor defaults it to empty
- `DonorAsset.FileManifest` is `IReadOnlyList<DonorFileEntry>` (relative path + byte size) - Phase 5 file slicing needs sizes; `_meta.json` is excluded by the importer/classifier
- `DonorAsset.ArchiveHash` must be non-empty; until Sprint 2.4 the classifier fills a documented `classification-pending` placeholder and the import service merges the real SHA-256

Source: `src/UltimateWardrobe.Core/Domain/DonorLibrary.cs:1`, `DonorAsset.cs:1`, `DonorFileEntry.cs:1`

### PieceMapping

```csharp
PieceMapping(Guid id, Guid overhaulId, string targetArmorSetId, string targetPieceEditorId, Gender targetGender, Guid donorAssetId, string donorPieceEditorId, string donorMeshPath, Guid? bodyPatch, Guid? physicsPatch, MappingStatus status, string? notes)
  UniqueKey => $"{OverhaulId}:{TargetPieceEditorId}:{TargetGender}"
  ValidateCrossProject(IReadOnlyCollection<DonorAsset> allowedAssets) // throws if donor/patches not in same project
```

Invariants enforced:

- `Id` / `OverhaulId` / `DonorAssetId` not empty; string fields not whitespace
- Uniqueness of `UniqueKey` is enforced at DB level in Phase 4 (`UNIQUE(OverhaulId, TargetPieceEditorId, TargetGender)`); domain helper `ValidateCrossProject` ensures same-project constraint now, full uniqueness check will be added with persistence
- Optional patch ids validated against same `allowedAssets` collection

Source: `src/UltimateWardrobe.Core/Domain/PieceMapping.cs:1`

Tests: `tests/UltimateWardrobe.Tests/Core/PieceMappingTests.cs:1`, `DonorAssetTests.cs:1`, `ArmorSetTests.cs:1`, `ProjectTests.cs:1`, `OverhaulTests.cs:1`

## Abstractions

All interfaces live in `src/UltimateWardrobe.Core/Abstractions/` and have no implementation in Core:

```csharp
// ArchiveAbstractions.cs:1
interface IArchiveExtractor { Task<ExtractResult> ExtractAsync(string archivePath, string destDir, IProgress<ExtractProgress>? progress, CancellationToken ct); }
class ExtractResult(IReadOnlyList<string> extractedFiles, int nestedHandled, ArchiveFormat format)
class ExtractProgress { int FilesDone; long BytesDone }

// ICatalogScanner.cs:1
interface ICatalogScanner { Task<Catalog> ScanAsync(CatalogSource source, CancellationToken ct); }

// IDonorClassifier.cs:1
interface IDonorClassifier { Task<DonorAsset> ClassifyAsync(string extractedDir, Catalog? catalogHint, CancellationToken ct); }

// IProjectStore.cs:1
interface IProjectStore { Task SaveAsync(Project project, CancellationToken ct); Task<Project> LoadAsync(string path, CancellationToken ct); }

// IPatcher.cs:1 (Sprint 5.0.2 amendment; shipped shape as of Phase 5)
interface IPatcher { Task<PatchResult> BuildAsync(Overhaul overhaul, DonorLibrary donorLibrary, string outputDir, IProgress<PatchProgress>? progress, CancellationToken ct); }
class PatchResult(string pluginPath, IReadOnlyList<string> copiedFiles) { PatchReport? Report }   // Report init-only, null until the orchestrator attaches it
class PatchReport { int TotalMappings; int ResolvedMappings; int SkippedMappings; int OverriddenRecords; IReadOnlyList<string> CopiedFiles; long CopiedBytes; IReadOnlyList<PatchWarning> Warnings }
class PatchWarning { string Message; string? Context }
class PatchProgress { string Stage; int Completed; int Total; string? Detail }
```

These are implemented in later phases: `Archives` (0.2), `Scanner` (1), `DonorLibrary` (2), `Persistence` (4), `Patcher` (5).

## Fixtures

Reusable helpers for tests and later phases - no I/O:

`tests/UltimateWardrobe.Tests/Core/Fixtures.cs:1` provides `CreateProject`, `CreateOverhaul`, `CreateArmorSet`, `CreateDonorAsset`, `CreateMapping`, `CreateCatalog`, `CreateVanillaSource`, `CreateStorySource`.

## Invariants Table

| Invariant | Enforcement | Test |
|-----------|-------------|------|
| Project owns exactly one DonorLibrary with same Id | Constructor creates Library | `ProjectTests.cs:11` |
| Overhaul.Source immutable | `init` accessor | `OverhaulTests.cs:10` |
| PieceMapping donor must be in same Project | `ValidateCrossProject` | `PieceMappingTests.cs:31` |
| Patch ids must be same Project | `ValidateCrossProject` checks both patches | `PieceMappingTests.cs:54` |
| Unique target piece per Overhaul | `UniqueKey` + future DB UNIQUE | `PieceMappingTests.cs:66` |
| All enums have Unknown=0 | Enum definition | `EnumTests.cs:17` |
| Empty strings / Guid.Empty rejected | Constructor guards | `ProjectTests.cs:22`, `DonorAssetTests.cs:17`, etc. |

## Test Coverage

- 47 tests pass on .NET 10 (`dotnet test -c Release`)
- Core has zero package references - verified via `dotnet list package` - no Mutagen / SQLite
- Negative paths (exception) are covered for every guard

## Next Phase

- Phase 6 - the `UltimateWardrobe.App` WPF desktop shell over the Phase 3 `MappingService` and the Phase 5 `IPatcher`: project open/save, the mapping UI (donor per target piece/gender, patch layers, per-set status + Overhaul progress) and the export flow behind a progress bar. Nothing in Phases 1-5 changes to host it. Sprint 6.1 done (`Docs/app.md`, `Plans/phase6.md`): `src/UltimateWardrobe.App` scaffolded (net10.0-windows, UseWPF, WinExe, v0.6.1), WPF-UI 4.3.0 + CommunityToolkit.Mvvm + Microsoft.Extensions.Hosting + Serilog, a shared `CompositionRoot.Register(services, registerUi)` (headless `registerUi=false` branch swaps the WPF adapters for null-stub test doubles), `MainWindow` FluentWindow + NavigationView shell (Project/Overhaul/Export placeholders + status bar) and the `ProjectPickerWindow` single-project startup gate (amendment 7) - the app owns ONE open Project per process via `IProjectSession`, with no in-app switch/close. The 4.3.0 wiring + spike conclusions (no plain Cancel symbol, no `ui:Page`, WPF implicit usings omit `System.IO`, `NavigationView.Navigate` needs the applied template) are recorded in `Docs/app.md`. Sprint 6.2 done (project + overhaul management): the `ProjectListViewModel` picker flow (recent list + remove, "New project" genuinely creates `project.db` via real `ProjectStoreFactory` + records recent + opens the session, "Open project" resolves an existing DB, missing-DB-on-open alerts and leaves the session untouched, canceled dialog no-ops), and the `ProjectViewModel` overhaul cards (name/progress/mapped-total/status, Add Vanilla via `IOverhaulSourceValidator.ValidateVanilla` against `Data\Skyrim.esm` + Add StoryMod with main-plugin/masters checks, per-card rename/delete/select that navigates to `OverhaulView`); persistence is wired so `IProjectSession.Store` is the ONE shared `IProjectStore` and every mutation autosaves through it (amendment 3). 27 new headless tests (RecentProjectsStore + OverhaulSourceValidator + ProjectListViewModel + ProjectViewModel + a reflection `CommandNameLeak` guard asserting no switch/close project command exists); full suite 619 tests green, Release 0 warnings / 0 errors. Sprint 6.3 done (donor library + drag-and-drop import): the `DonorLibraryViewModel`/`DonorLibraryView` table (original file name, Kind badge, ProvidedSets count, BodySlide/physics indicators, import date, per-row Remove/Reclassify/Set Kind commands over the injected concrete `DonorLibraryService`) plus the import drop zone (`.7z/.zip/.rar` multi-file) that extracts/classifies/appends each archive through the Phase 2 pipeline on `IBackgroundTaskService` via the App-layer `IDonorImportRunner` seam with a per-file progress bar + cancel; a failed archive -> typed alert with the library snapshot unchanged (Phase 2 already cleaned up), a cancelled batch is silent, and every mutation autosaves through the shared `IProjectSession.Store`. 24 new headless tests (`DonorImportRunnerTests` over a stub-extractor/classifier `DonorLibraryService` + `DonorLibraryViewModelTests` + a scriptable `IDonorImportRunner` double + `CommandNameLeak`); full suite 643 tests green, Release 0 warnings / 0 errors. The mapping matrix grid (6.4), the anchored-popover cell editor (6.5) and the export screen (6.6) land in later sprints. Next Phase: 7 - story-mod targeting as Target.

- Phase 2 - `UltimateWardrobe.DonorLibrary` implements `IDonorClassifier` with graduated classification (branch 1 - the Phase 1 pipeline over donor plugins with reference-master enrichment; branch 2 - mesh/texture heuristics; branch 3 - BodySlide/physics detection + `DonorAssetKind`) plus the `DonorLibraryService` import/remove/reclassify flow with the cross-project guard. Sprint 2.5 froze the `DonorAsset` shape under a four-archetype synthetic golden suite (regens via `UW_WRITE_GOLDENS=1`) and an Integration-gated real-donor suite. See `Docs/donor-library.md`.
- Phase 3 - `UltimateWardrobe.Mapping` implements the manual mapping `MappingService` over the existing `PieceMapping`/`ArmorSetStatus` domain (Sprints 3.0-3.4 done, Phase 3 complete): it assigns a donor per target piece/gender, attaches body/physics patch layers, derives `NeedsPatch` from the combined donor-or-patch `Detected*` flags plus `Overhaul.Policy` (via `NeedFor`/`BodyMarkerFromPath`), recommends patches deterministically, computes per-set `ArmorSetStatus` and Overhaul progress, and toggles the caller-side `Done` overlay via `SetDone` (`Done` is an overlay, not a derived status). Sprint 3.4 added a determinism suite (same assign sequence -> same statuses/progress every run) and an Integration-gated real-donor spot-check (Red Hood - HIMBO: `BodyConversionPatch`, 2 BodySlide + 10 physics flags -> a piece mapped to it reads `None`/`Mapped` under `RequireBodyConversion`/`RequirePhysics`). Core gained the additive `PatchPolicy` enum + `Overhaul.Policy` (init-only, default `Loose`). Next Phase: 4 (persistence - `Overhaul.Mappings`/`Catalog`/`DonorAsset` to a project DB, DB-level `UNIQUE` on `UniqueKey`). See `Docs/mapping.md`.
- Phase 4 (done, Sprints 4.0-4.4) - the new `UltimateWardrobe.Persistence` project persists the in-memory state to a SQLite `project.db`. Sprint 4.0 stood up the project (project + `Microsoft.Data.Sqlite` 10.0, registered in slnx and Tests), the `ProjectDatabase` open/create bootstrap (`WAL` journaling + `foreign_keys=ON` on every connection, `Pooling=False`, empty-DB detection, typed `ProjectStoreException`), and the Persistence-local JSON layer (`PersistenceJson` + `CatalogSourceJsonConverter` + row codecs ready for the 4.1 schema). Sprint 4.1 added the migration engine (`IMigration`/`IMigrator`/`Migrator`): `M001_Initial` creates the section 4.2 schema (SchemaVersion + Project/Overhaul/DonorAsset/PieceMapping/CatalogCache + FK + `UNIQUE(OverhaulId, TargetPieceEditorId, TargetGender)` = `UniqueKey` + indexes), applied on `OpenAsync` - fresh DB runs `M001`, an existing DB runs only missing versions each in its own transaction, a DB newer than the app fail-fast refuses to downgrade, and an existing schema is backed up to `project.db.bak` before any upgrade. Sprint 4.2 added the repository layer: `UnitOfWork` (transaction boundary over the single connection; `BeginAsync` re-issues `PRAGMA defer_foreign_keys=ON` per the 4.3.1 transaction-level note), `RowCodecs`, and the five repositories (`ProjectRepository`, `OverhaulRepository` with `SourceJson` + `Policy` + timestamps, `DonorAssetRepository` with JSON columns + `Kind` and an explicit `projectId` since the domain carries none, `PieceMappingRepository` with ON CONFLICT on `UniqueKey` so a second assign replaces, `CatalogCacheRepository` with the whole-`Catalog` JSON per Overhaul); a DB-level FK test proves a mapping to a donor not in the DB is rejected only because `foreign_keys=ON` is set (issue 3). Sprint 4.3 completed the Phase 0.2 `IProjectStore` facade over the repositories: `SaveAsync(Project)` persists the whole graph (Project row + `Library.Assets` + each Overhaul + its Mappings + `Catalog` cache) in ONE `UnitOfWork` transaction, upsert-only by stable domain id (issue 3 - no delete-then-reinsert; a re-save never duplicates rows), with `BeginAsync` re-issuing `PRAGMA defer_foreign_keys=ON` inside EVERY new transaction (the pragma resets to OFF at commit/rollback - proven by a 3-cycle pragma test on one connection). `LoadAsync(path)` opens + migrates and rebuilds the full graph; each `Overhaul` is reconstructed so its `init`-only `Catalog` can be set from `CatalogCache`; a DB with no Project row, an unreadable file, or a newer schema surfaces as a typed `ProjectStoreException` (never a crash). Round-trip tests assert deep equality (Project/Overhaul/DonorAsset/PieceMapping + catalog cache, incl. a `BodyConversionPatch` with detected flags and an attached body patch) and that `MappingService.GetArmorSetStatus`/`GetOverhaulProgress` on the reloaded graph equal the pre-save results. Core gained the additive Overhaul amendments: 4.0.2 `CreatedAt`/`ModifiedAt`, 4.3 `Catalog` (init, nullable). Next: Sprint 4.4 (Integration round-trip + `Docs/persistence.md` + README close-out). Sprint 4.4 added the Integration-gated end-to-end spot-check (`ProjectStoreIntegrationTests`): a REAL donor (Red Hood - HIMBO, `BodyConversionPatch` with real BodySlide + physics flags) imported + classified, mapped (assigned synthetic donors + the real donor ATTACHED as the body patch layer), `SaveAsync`'d, reopened in a fresh `ProjectDatabase`, and `LoadAsync`'d - the reloaded graph deep-equals the original, `MappingService.GetArmorSetStatus`/`GetOverhaulProgress` are identical, and the extracted donor folder path still resolves. Core gained the additive Overhaul amendments: 4.0.2 `CreatedAt`/`ModifiedAt`, 4.3 `Catalog` (init, nullable). See `Docs/persistence.md`. Next Phase: 5 (patcher - the persisted `Project` graph is the input; an `UltimateWardrobe.Patcher` esp + file-slicer build consumes it).
- Phase 5 (done, Sprints 5.0-5.4) - `UltimateWardrobe.Patcher` consumes the persisted Phase 4 graph: the Core `IPatcher` amendment (`BuildAsync(Overhaul, DonorLibrary, outputDir, IProgress<PatchProgress>?, ct)`, `PatchResult.Report` init-nullable, `PatchReport`/`PatchWarning`/`PatchProgress` records in `Core/Abstractions/IPatcher.cs` - additive, existing callers unaffected) and the `TargetResolver`, which resolves each `PieceMapping` to a live (ARMO, ARMA) pair over the Phase 1 pipeline (`PluginDiscovery` + `LoadOrderBuilder` + `ModLoader` + `RecordIndex`, scanning the `Overhaul.Catalog` source): ARMO by EditorID primary / FormId fallback, ARMA by `Piece.ArmaEditorId` then the first armature addon, gendered `Model` paths extracted normalized for the amendment #6 loose-path skip. A missing `Overhaul.Catalog` or an unreadable source root throws a typed `PatchException`; per-mapping unresolved targets are skipped with a `PatchWarning` (build continues). Sprint 5.1 adds the `PluginBuilder`: per-target ARMA override records via `GetOrAddAsOverride` (re-resolved by FormKey over a freshly loaded source, getters kept alive for the write), gendered `WorldModel` `GivenPath` writes created for missing slots, an ordinal-guarded `UW_` EditorID prefix, the amendment #6 loose-path skip (`PatchPathRules.EqualsNormalized`; null current path never skips), the amendment #7 ESL gate (`IsSmallMaster` for the Vanilla + DLC source kind, survives the binary round-trip), amendment #8 mesh shadowing (`ResolveEffectiveMesh`, body/physics patch last-wins) and `WriteToBinary` with auto-collected masters. Sprint 5.2 adds `DonorFileLocator` (game-relative -> physical file, root-or-`Data/` layout, traversal guard, missing -> warning) and the `FileSlicer`, which copies exactly the whitelisted set per mapping - the amendment #8 effective mesh (an internal `PluginBuilder.ResolveEffectiveMesh` keeps the plugin record and the loose slice in agreement) + `_1st`/`_1stperson` alternates (`MatchStem` strips the weight/first-person markers, implemented locally under amendment #4; `_0`/`_1` weight variants are intentionally not sliced) + provided-set textures or the folder-mirror `textures/**/*.dds` fallback + matching `DetectedBodySlideFiles` + physics (the attached physics patch's `SKSE/Plugins/**`, else the donor's matching detection) - plus the body-then-physics patch overlays (last wins: collision, `CalienteTools/BodySlide/**` / `SKSE/Plugins/**`, or mirrored meshes; junk excluded). The whole export is de-duplicated per output-relative path and copied once in ordinal order; a missing primary mesh skips the mapping with a warning (nothing partial), other missing files warn; cancellation is honored between mappings and copies; a physical copy failure is a typed `PatchException`. Covered by 10 resolver tests + 11 plugin-builder tests (plugins reopened with `CreateFromBinaryOverlay`) + 13 `FileSlicerTests` over runtime-synthesized donor folders (exact whitelist, dedup, overlay shadow, physics-from-patch, missing-mesh skip, traversal rejection, root/`Data/` parity, cancellation, non-mapped skip) + 11 `OutputFolderTests` (ModName sanitizer, `ResolveModDir` containment + typed `PatchException`s, `ClearModDir` stale-clear + lock failure, `meta.ini` exact layout + differing-`generated` two-timestamp lock) + 5 `WardrobePatcherTests` (2-mapping E2E asserting the exact 15-entry mod-folder tree, reopened-esp overrides + ESL + masters, report numbers, two-run determinism with byte-identical esp + identical report, re-export cleanliness with planted orphan meshes/`.bsa`/stale `meta.ini` fields gone, and the locked-folder / missing-catalog typed `PatchException`s). Sprint 5.3 closes the full loop: `OutputFolder` (the section 4.6 mod folder - sanitized `ModName`/plugin file name, `ResolveModDir` verifying the path stays strictly under `outputDir` with a typed `PatchException` before anything is touched, `ClearModDir` delete-then-rebuild with a typed `PatchException` for lock failures, and `WriteMetaIni` - `[General]`/`name`/`version=1.0.0`/`category`/`notes` + a `generated=<yyyy-MM-dd HH:mm:ss UTC>` stamp that differs between runs) and the `WardrobePatcher` orchestrator implementing `IPatcher.BuildAsync` (resolve -> `ResolveModDir` + `ClearModDir` -> `PluginBuilder.Build` -> `FileSlicer.Slice` over the resolved mappings only -> `WriteMetaIni` with the distinct-resolved-`TargetArmorSetId` count -> a composed `PatchReport` with total/resolved/skipped/overridden/copied-bytes + concatenated warnings; 5 coarse `PatchProgress` stages). Sprint 5.4 closes Phase 5 with the real-data Integration spot-check (real donor import + classify + real-game scan + full `BuildAsync`, esp re-open, master set exactly `[Skyrim.esm]`, physical donor-file resolution, exact output tree) and the xEdit validation proxy (manual hand-check result recorded in `Plans/phase5.md`). Phase 5 complete, 589 tests green. Next Phase: 6 (WPF app shell - the `UltimateWardrobe.App` desktop UI over the Phase 3 `MappingService` and the Phase 5 `IPatcher`, see the "Next Phase" section above). See `Docs/patcher.md` and `Plans/phase5.md`.
