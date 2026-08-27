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
```

- `Source` is immutable after construction - changing source requires a new `Overhaul`
- Guards: empty ids / name / null source

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
DonorProvidedSet(string id, string displayName)
DonorAsset(Guid importId, string originalFileName, string extractedPath, DateTime importedAt, string archiveHash, DonorAssetKind kind, IReadOnlyList<DonorProvidedSet>? providedSets, IReadOnlyList<string>? fileManifest, IReadOnlyList<string>? detectedBodySlideFiles, IReadOnlyList<string>? detectedPhysicsFiles)
```

Source: `src/UltimateWardrobe.Core/Domain/DonorLibrary.cs:1`, `DonorAsset.cs:1`

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

// IPatcher.cs:1
interface IPatcher { Task<PatchResult> BuildAsync(Overhaul overhaul, string outputDir, CancellationToken ct); }
class PatchResult(string pluginPath, IReadOnlyList<string> copiedFiles)
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

- Sprint 0.2 - `UltimateWardrobe.Archives` implements `IArchiveExtractor` with P/Invoke over `runtimes/win-x64/native/7z.dll` and `UnRAR64.dll`
