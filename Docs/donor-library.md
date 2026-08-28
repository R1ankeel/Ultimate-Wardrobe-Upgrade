# Donor Library: Import + Classification

> Phase 2, Sprints 2.0-2.1 done - `src/UltimateWardrobe.DonorLibrary` - graduated classification for extracted donor folders: branch 1 runs the Phase 1 scanner pipeline over donor plugins (optionally enriched with reference game esms), branch 2 falls back to mesh/texture heuristics (lands in Sprint 2.2), branch 3 adds BodySlide/physics detection and `DonorAssetKind` (lands in Sprint 2.3).

## Overview

`DonorClassifier.ClassifyAsync(string extractedDir, Catalog? catalogHint, CancellationToken)` turns an extracted donor folder `Source/<ImportId>/` into a typed `DonorAsset`. The classification reuses the exact `ArmorSet/Variant/Piece` shapes of the Phase 1 catalog, so a donor-provided set is directly comparable with a catalog `ArmorSet` in the Phase 3 mapping UI.

The code reuses Phase 0.1/0.2 (`UltimateWardrobe.Archives` - extraction, `_meta.json`, manifest) and the Phase 1 grouping heuristic (`KeyNormalizer`, `PieceTypeDetector`, `GenderWeightDetector`, `ArmorCorrelator`, `ArmorSetGrouper`, `VariantAssembler`, `ModLoader`, `RecordIndex`, `ScanReportBuilder`) as-is - `DonorLibrary` depends on `Core + Archives + Scanner` (recorded deviation from the "only Core" dependency rule, see `Plans/phase2.md` `## 2`).

## Import flow

```
User drops an archive
  -> Archives.DonorImportService.ImportAsync(archivePath, projectRoot)   # extract + SHA-256 + manifest (entries with sizes) + _meta.json
  -> DonorClassifier.ClassifyAsync(asset.ExtractedPath, catalogHint)     # graduated 3-branch classification
  -> DonorLibraryService adds the merged DonorAsset to Project.DonorLibrary.Assets   # lands in Sprint 2.4
```

`IDonorClassifier` is an "extracted folder" adapter: it builds a `DonorAsset` purely from the folder. The classifier fabricates a documented placeholder archive hash (`classification-pending`) because `DonorAsset` forbids an empty `ArchiveHash`; `DonorLibraryService` (Sprint 2.4) merges the real archive identity (file name, hash, timestamps) on top of the classification result.

## Components

| File | Purpose |
|------|---------|
| `DonorClassifier.cs:1` | `IDonorClassifier` - branch routing, final `DonorAsset` assembly, fall-through (2.1.4) |
| `DonorPluginProbe.cs:1` | Plugin discovery inside the extracted folder: candidates, frozen main-plugin rule, masters (2.0.5) |
| `DonorScanPipeline.cs:1` | Branch 1: reference + donor load list, donor-only ARMO filter, correlate -> group -> assemble -> `DonorProvidedSet`s (2.1.2) |
| `ReferenceMasterMerger.cs:1` | Reference game esms merged into the load set for index-only resolution, deduped against the donor set (2.1.1) |
| `MeshPathIndexer.cs` | Branch 2: `meshes/**/*.nif` + `textures/**/*.dds` folder grouping (Sprint 2.2, not yet present) |
| `DonorNameHeuristics.cs` | Gender/weight/piece tokens from paths and stems (Sprint 2.2, not yet present) |
| `BodySlideDetector.cs` | `CalienteTools/BodySlide` globbing (Sprint 2.3, not yet present) |
| `PhysicsDetector.cs` | hdt/cbpc/physics/tri globbing (Sprint 2.3, not yet present) |
| `DonorKindDetector.cs` | `FullReplacer | BodyConversionPatch | PhysicsPatch | Unknown` (Sprint 2.3, not yet present) |
| `DonorLibraryService.cs` | `ImportAsync` / `RemoveAsync` / `ReclassifyAsync`, project guard (Sprint 2.4, not yet present) |

## Branch routing (DonorClassifier)

```
probe = DonorPluginProbe.Probe(extractedDir, warnings)
probe.Main is null  -> branch 2 (mesh heuristics)            # no .esp/.esm/.esl in the folder
else                -> branch 1 (plugin pipeline, 2.1)
branch 1 yields 0 ProvidedSets -> warning + log + branch 2   # fall-through (2.1.4)
```

The assembled `DonorAsset`:

- `ImportId` = the `Source/<ImportId>/` folder name when it parses as `Guid`, else a fresh one
- `OriginalFileName` = folder name, `ExtractedPath` = the folder, `ImportedAt` = UTC now
- `ArchiveHash` = `classification-pending` placeholder until Sprint 2.4
- `Kind` = `Unknown` until Sprint 2.3 (stays honest - no fabricated kind)
- `ProvidedSets` = branch output
- `FileManifest` = relative paths (slash-normalized) + sizes, `_meta.json` excluded, ordinal by path
- `DetectedBodySlideFiles` / `DetectedPhysicsFiles` = empty until Sprint 2.3

Branch 1 with zero sets means "the donor esp carries no groupable armor (no ARMO, missing masters, or every armor was skipped)". A `LogLevel.Warning` reason plus a `ScanWarning` are emitted before routing to branch 2.

## Sprint 2.0 - scaffolding + Core amendments + classification skeleton

### Core amendments (2.0.2, Scope amendment #1/#2)

- New `DonorFileEntry { string RelativePath; long Length; }` record (validated: relative path non-empty, length >= 0).
- `DonorAsset.FileManifest` retyped from `IReadOnlyList<string>` to `IReadOnlyList<DonorFileEntry>` - Phase 5 file slicing needs sizes to deduplicate and report copied bytes.
- `DonorProvidedSet.Variants` added (`IReadOnlyList<Variant>`, `init`); the 2-arg `(id, displayName)` ctor defaults it to empty.

`UltimateWardrobe.Archives.DonorImportService` is the only archive-side producer: it now emits manifest entries with per-file sizes (relative path slash-normalized + `FileInfo.Length`), ordinal by path. `_meta.json` carries `extractedFilesCount` unchanged.

### Project + package (2.0.3)

`UltimateWardrobe.DonorLibrary` (`net10.0-windows`): project references `Core + Archives + Scanner`; package references `Mutagen.Bethesda.Skyrim 0.54.4` + `Microsoft.Extensions.Logging.Abstractions 10.*`; the NU190x `WarningsNotAsErrors`/`NoWarn` pattern is mirrored. Registered in `UltimateWardrobe.slnx` and in the Tests project.

### DonorPluginProbe - frozen main-plugin rule (2.0.5)

- Candidate enumeration: `*.esm | *.esl | *.esp` in the root and in `Data/` when present (same layout rule as `PluginDiscovery.ResolveDataPath`). Candidates ordered ordinal by `ModKey.Name`.
- `DataPath` resolves to `Data/` when it exists, else the extracted root.
- Main-plugin rule (frozen, deterministic): a candidate is "main" when no other candidate lists it in its `MasterReferences` (read via `ModLoader.ReadMasters`); among qualified candidates prefer `.esp` over `.esl` over `.esm`, then ordinal by name. A pure master chain (every candidate referenced) reduces to the same extension/ordinal tie-break. Corrupt plugins warn and are treated as master-less (last-choice candidates). A no-plugin folder yields an empty probe.
- Deterministic, unit-tested (10 tests).

### Classifier skeleton (2.0.4)

Implements `IDonorClassifier`; validates `extractedDir` (friendly `DirectoryNotFoundException`); routes on `probe.Main`; branch 2 stub returns empty until 2.2; `Kind = Unknown`, empty flags. Mutagen note: `ModKey.FileName` is a `FilePath`, not a `string` - ordering uses `.Name` and the extension rank uses `.ToString()`.

## Sprint 2.1 - classify via plugin + reference-master merge

### ReferenceMasterMerger (2.1.1)

`Merge(referenceRoot, donorKeys)` returns the ordered list of reference plugin `*.esm|*.esl` (top-level only) from the reference root - the `Data/` layout, or the root layout when `Data/` is absent. Rules:

- Reference is purely optional enrichment: a missing or empty reference root merges nothing.
- A reference file whose name the donor set owns is excluded - the donor's bundled copy loads instead and wins (donor later-wins).
- Within the reference itself, duplicate names keep the ordinal-first file (deterministic).
- Output is deterministic, ordinal by file name.

### DonorScanPipeline (2.1.2)

`Run(probe, referencePaths, warnings, ct)` runs the branch-1 pipeline:

- Combined load list = `[reference paths] + [donor candidates]`; a reference path whose name the donor set owns is dropped here too, so a caller-provided unfiltered list cannot resurrect a donor-bundled duplicate.
- Reference first, donor later-wins in `RecordIndex` (override semantics) - reference records resolve keyword/armature/race/outfit/TXST links while donor records win on equal FormKey.
- `EnumerateArmor` filtered to `donorKeys.Contains(a.FormKey.ModKey)` - only donor-originated ARMO is correlated; reference armor never reaches the output (2.1.3 curation rule).
- Each record: `ArmorCorrelator(new FileResolver(probe.DataPath)).CorrelateOne`; then `ArmorSetGrouper.Group` and `VariantAssembler.Assemble`, each wrapped in `ScanReportBuilder.Guard` so one broken record never aborts classification.
- Each `ArmorSet` maps to `DonorProvidedSet(Id, DisplayName, Variants)` - `Id`/`DisplayName` from the set, `Variants` reused as-is.
- Result: `DonorPipelineResult { ProvidedSets, DonorArmorCount, LoadedPluginCount, ReferencePluginCount }`.

### Load/dispose + corrupt discipline (2.1.3)

Loaded `LoadedMod` overlays (reference + donor) are disposed in a `finally`. Corrupt reference/donor plugins warn and are skipped via `ModLoader.TryLoad` - never abort. Cancellation is checked between plugins and between stages.

### Fall-through (2.1.4)

A donor plugin that yields zero sets (or the reference-dependent case without a hint) falls through to branch 2 with a logged reason, mirroring the risk table of `Plans/phase2.md` `## 7`.

## Determinism

- Probe ordering, reference merge ordering, ARMO ordering (`ModKey.Name` ordinal, then `FormKey.ID`), and manifest ordering (ordinal by relative path) are all stable.
- A plugin folder re-classifies byte-identically: the classifier determinism test re-runs `ClassifyAsync` on the same folder and asserts the produced `DonorAsset` shape (projected `ProvidedSets` + `FileManifest`) is equal.
- Timestamps and archive hashes are excluded from equality scope (placeholder hash until 2.4).

## Mutagen notes

- FormIDs below the lower range (0x800) are disallowed for records in a master's own namespace - `WriteToBinary` throws `LowerFormKeyRangeDisallowedException`. Test fixtures place reference master records at 0x9xx.
- `ModKey.FileName` is a `FilePath` - use `.Name` for ordinal file-name ordering and `.ToString()` for extension rank.

## Tests

- `tests/UltimateWardrobe.Tests/DonorLibrary/DonorModBuilder.cs` - runtime builders via the Mutagen writer: self-contained esp (`DonorKit.esp`), bundled master pair (`BundledBase.esm` + `BundledKit.esp`), reference base (`RefBase.esm`) + reference-dependent esp (`DonorRef.esp`), empty esp, esm/esl writers.
- Sprint 2.0: 18 new tests (2 Core amendments + 10 `DonorPluginProbeTests` + 6 `DonorClassifierTests`) - empty folder -> `Unknown` with 0 sets and a manifest, missing-folder throw, loose-files (no plugins) -> manifest sizes, probe determinism, master chain, corrupt-plugin-as-candidate, layout variants.
- Sprint 2.1: 16 new tests (6 `DonorReferenceMasterMergerTests` + 9 `DonorScanPipelineTests` + 1 classifier-net) - self-contained donor classifies into the expected set with Male + Female Heavy variants and manifest coverage; keyword record inside a bundled fake master resolves; plugin with 0 ARMO falls through to branch 2; reference resolves a keyword without leaking reference armors; reference-dependent donor without a hint falls through; donor-bundled copy wins over a same-named reference; corrupt donor warns and falls through; corrupt reference warns and is skipped; pipeline reports donor/reference counts.
- Full suite 362/362 green (was 346, +16), Release 0 warnings/0 errors, no temp `UW_Donor_*` dirs, no `TestResults/`.

## Status

Sprints 2.0-2.1 are complete: import-time manifest, plugin probe, branch-1 classification with optional reference enrichment, and deterministic classification for esp-carrying donors. Branch 2 (mesh/texture heuristics), branch 3 (BodySlide/physics/kind), the `DonorLibraryService` import flow, golden snapshots, real-donor integration tests, and the final docs pass land in Sprints 2.2-2.5.