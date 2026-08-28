# UltimateWardrobe (WIP - not usable)

Ultimate Wardrobe Upgrade - Product Description

What It Is

UltimateWardrobe is a standalone application for creating complete visual replacers for armor and clothing in Skyrim SE without requiring access to the user's actual load order.

The application does not "patch the game" in the traditional sense of modding tools. Instead, it turns a collection of separate donor mod archives - which would normally have to be manually extracted, sorted, and adapted for BodySlide and physics - into a single, cohesive replacer mod ready for installation through Mod Organizer 2.

The product addresses a specific and familiar problem for anyone who has built a large modlist: the desire to give the entire game a consistent visual style, for example, having all armor use 3BA/HIMBO with consistent physics, without spending months manually dealing with xEdit, conflicting patches, and separate BodySlide presets for every individual armor set.

Who It Is For

The tool is intended for users who:

Build large modlists and want a consistent visual style for all armor and clothing in the game.
Maintain several independent builds in parallel, for example, different PCs with different visual concepts, and do not want work on one build to interfere with another.
Want to distribute the result as a ready-to-use replacer mod for other people's builds without tying it to gameplay balance or level lists - meaning Iron Armor remains completely vanilla in terms of its gameplay properties and simply looks different.
How It Works - User Workflow

The user creates a Project - a workspace for one independent build.

Each Project has its own internal library of imported donor mods, completely isolated from other Projects. This makes it possible to maintain several completely independent visual concepts in parallel without mixing or cross-contaminating them.

Inside a Project, the user creates one or more Overhauls - each targeting a specific armor source: vanilla Skyrim + official DLCs, or a story expansion such as Vigilant or Lordbound.

All the user needs to do is provide the application with a folder - either the installed Skyrim directory or an extracted story mod - and the application automatically analyzes all armor and clothing it contains, organizing them into recognizable sets such as Iron Armor, Vigilant's Cuirass, and so on, while also separating them by gender and weight categories.

The user then simply downloads donor mods - donor archives containing, for example, a specific replacement for Iron Armor - and adds them to the application.

UltimateWardrobe automatically extracts the archive, analyzes its contents, and organizes the discovered armor according to the same principles used for the original source armor.

The user then manually maps the items: "this is the vanilla Iron Armor for the male model - this is the donor set that should replace it." The same mapping can be configured separately for the female model if the donor differs between genders.

If the donor already contains ready-made BodySlide presets and physics support, the application detects and uses them automatically. If something is missing, it asks the user to provide the required patch, such as a conversion for the target body or physics system. That patch then becomes part of the same mapping.

The user works through all armor sets in an Overhaul at their own pace - however many they can complete in one session.

Progress is saved continuously, so a large build containing hundreds of armor sets can be completed incrementally: save the project, close the application, return the next day, and continue exactly where you left off, with a clear understanding of what has already been completed and what remains.

Once an Overhaul is fully configured, the user can press a single button to build the final mod.

The application extracts only the files actually used from the donor mods, excludes everything unnecessary, and packages the result into a ready-to-install mod structure.

Each Overhaul within a Project is exported as a separate standalone mod. For example, a vanilla armor replacer and a Vigilant armor replacer are not merged into a single monolithic All-in-One package. They remain separate mods that the user can install together or independently.

Core Product Principles
Complete Independence from the Game and Its Load Order

The application does not need access to the user's actual load order, plugin manager, or running game. It only needs a path to the relevant folder on disk.

This makes the tool simple, predictable, and safe to use. It cannot break an existing build because it does not directly interact with it in the first place.

Visual Layer Only - Zero Risk to Gameplay Balance

The replacer does not create new items, modify armor stats, touch keywords, weight, or item classification.

Iron Armor remains the exact same Iron Armor as in the original game - with all of its crafting recipes, level-list entries, and compatibility with any balance mods.

The only things that change are how the armor looks and how it physically behaves on the character's body.

Separate, Self-Contained Results

Each Overhaul is a separate, independently installable mod.

The user does not have to build one enormous AIO package. A vanilla armor replacer, a Vigilant armor replacer, and a Lordbound armor replacer are three separate mods that can be combined however the user wants.

Designed for Large-Scale Work

A complete visual overhaul of the game means working with hundreds of armor sets, which is not something that can realistically be completed in a single sitting.

The product is designed around this reality: progress is saved continuously, the status of every armor set is visible at any time, and unfinished work can be resumed as many times as necessary.

Extensibility Without Reworking the Foundation

Although the first complete version of the product focuses on armor and clothing for vanilla Skyrim and its official DLCs, the architecture is designed from the beginning to support the same workflow for major story expansions.

Adding support for projects such as Vigilant or Lordbound does not require fundamental changes to how the tool works.

Standalone application for building full visual replacers for Skyrim SE armor and clothing - without requiring connection to the user's real load order.

## Status

- Phase 0 - Foundation: done
  - Sprint 0.0 scaffolding - done (solution builds on .NET 10, native DLLs wired)
  - Sprint 0.1 domain model - done (`UltimateWardrobe.Core` with POCOs/enums/abstractions, 47 tests green)
  - Sprint 0.2 archive layer - done (`UltimateWardrobe.Archives` native-first over 7z.dll (7z/zip) + UnRAR64.dll (rar) with SharpCompress fallback, 96 tests green)
- Phase 1 - Folder Catalog Scanner (Mutagen): done
  - Sprint 1.0 scaffolding + spike - done (`UltimateWardrobe.Scanner` scaffolded, real `Skyrim.esm` read path proven via overlay, spike conclusions + Slot/gender-signal freezes recorded in `Plans/phase1.md`)
  - Sprint 1.1 plugin loading + RecordIndex - done (`PluginDiscovery`, `LoadOrderBuilder`, `ModLoader`, `RecordIndex`; synthetic pair maps a main-plugin FormID to the override record, corrupt plugin does not abort the scan; 121 tests green)
  - Sprint 1.2 ARMO -> ARMA -> files correlation - done (`FileResolver` path matrix, `ArmorCorrelator` pre-Piece with mesh/texture paths from TXST, unresolved-link/missing-master warnings never abort; 134 tests green)
  - Sprint 1.3 ArmorSet grouping heuristic - done (`PlayableRaceFilter` creature-skin pre-filter, `OutfitSetKeyResolver` Outfit-first signal with EDID/mesh fallback, `KeyNormalizer` + `PieceTypeDetector`, `ArmorSetGrouper` with per-reason skip breakdown; split-membership set lands in ONE `ArmorSet`; 226 tests green)
  - Sprint 1.4 Gender / Weight split - done (`BipedSlotMapper` frozen slot table, `GenderWeightDetector` weight from KEYW with ArmorType bonus + gender from ID/mesh/ARMA signals with Unisex fallback warning, `VariantAssembler` one variant per (Gender, Weight) with same-ARMO two-Piece split; Iron acceptance produces Male Heavy + Female Heavy; 270 tests green)
  - Sprint 1.5 Catalog model + cache - done (`FolderCatalogScanner.ScanAsync` pipeline with cancellation, deterministic `Catalog`; `ScanReport` warning dedup/sort + `ScanStats` + per-record exception routing; `CatalogCacheStore` canonical JSON + `CatalogSource` converter + `IsFresh` probe invalidation; round-trip value identity + stale-probe invalidates cache; 297 tests green)
  - Sprint 1.6 Tests + goldens - done (committed static golden plugin `tests/TestData/Plugins/MiniUniverse.esp` + golden catalog JSON regenerated via `UW_WRITE_GOLDENS=1`; snapshot compare normalizes the scan root to `<root>`; negative-path suite for corrupt/empty/missing-master plugins; `PlayableRaceFilter` whitelist corrected to real RACE EditorIDs (`NordRace` etc.) + `DefaultRace` universal fallback, verified on the real vanilla scan; Integration-gated real-data tests - vanilla scan (4197 ARMO, 3360 sets, ~1.2 s) + VIGILANT story-mod scan from rar with cleanup; full suite 318 tests green)
  - Sprint 1.7 Logging, reporting, docs - done (`FolderCatalogScanner` structured `ILogger<T>` milestone events + `ListLogger<T>`; `ScanReport` on `Catalog.Report` + `BuildSummary()` via `ScanReportBuilder`; heuristic tuning pass - trailing-variant-letter strip (`ArmorSteelBootsA` -> `steel`) + wardrobe-outfit filter so NPC composition outfits like `cwmission04outfitimperial` no longer swallow unrelated armor families; real vanilla 4197 ARMO -> 3396 sets in ~1.5 s with the plain Iron/Steel/Leather kits intact as single sets, guarded by `Vanilla_RealGame_FullKitsAreSingleSets_NoMegaSets`; `Docs/scanner.md` rewritten for the final pipeline; full suite 328 tests green)

- Phase 2 - Donor Library - import + classification: done
  - Sprint 2.0 scaffolding + Core amendments + classification skeleton - done (`UltimateWardrobe.DonorLibrary` project; Core amendments `DonorFileEntry` + `FileManifest` entries with sizes + `DonorProvidedSet.Variants`; `DonorImportService` size-emitting manifest; `DonorPluginProbe` with the frozen main-plugin rule; `DonorClassifier` skeleton routing branches + `Unknown` kind; 346 tests green)
  - Sprint 2.1 classify via plugin + reference-master merge - done (`ReferenceMasterMerger` merging reference game esms into the load set without leaking reference armor; `DonorScanPipeline` donor-only ARMO -> correlate -> group -> assemble -> `ProvidedSets`; fall-through to branch 2 when branch 1 yields 0 sets; corrupt plugins warn + skip; 362 tests green)
  - Sprint 2.2 mesh/texture heuristics (branch 2) - done (`MeshPathIndexer` recursive `meshes/**/*.nif` + `textures/**/*.dds` globbing over root + `Data/` layouts; `DonorNameHeuristics` piece/gender/weight from stems + paths with `_0/_1/_1st` normalization and `_1 > _0 > _1st` primary-file preference; `MeshSetAssembler` ProvidedSets from mesh groups with TXST-like texture linkage - esp-less donors now classify; 407 tests green)
  - Sprint 2.3 BodySlide/physics detection + Kind (branch 3) - done (`BodySlideDetector` SliderSets `*.osp` + SliderGroups `*.xml` + BodySlide-root `.xml`; `PhysicsDetector` hdtSMP/cbpc/physics tokens, `SKSE/Plugins` configs + `.tri` under detected mesh sets; `DonorKindDetector` FullReplacer/BodyConversionPatch/PhysicsPatch/Unknown with flags independent of Kind; branch 3 runs for every classification; 434 tests green)
  - Sprint 2.4 `DonorLibraryService` import flow - done (`DonorLibraryService` wired to the real archive extractor: `ImportAsync` extract -> classify -> merge identity -> guard -> append with failure cleanup, `RemoveAsync` deletes the extracted folder, `ReclassifyAsync` re-runs an existing folder with an optional reference-carrying `catalogHint`; cross-project guard rejects an archive owned by this or another library via `DonorAlreadyOwnedException`; 441 tests green)
  - Sprint 2.5 tests + goldens + real-donor integration + docs - done (four-archetype `SyntheticDonorUniverse` with fixed Guids; committed `tests/TestData/DonorGolden/*-donor.json` golden snapshots regenerated via `UW_WRITE_GOLDENS=1` and deep-equal-compared; `RealDonorIntegrationTests` Integration-gated with auto-skip + loose asserts + branch-2 esp-less gate - branch-2 fixture is genuinely esp-less (`Red Hood - HIMBO`), and the Execution Log records the real-donor probe findings incl. that 'EBONWRAITH/Gryphon Knight' carry no detectable physics; full suite 455 tests green, Release 0 warnings/0 errors)
- Phase 3 - Manual Mapping + Physics / BodySlide: done
  - Sprint 3.0 scaffolding + Core amendment + Mapping skeleton - done (`UltimateWardrobe.Mapping` project depending on `Core` only; Core amendment `PatchPolicy` enum (`Loose/RequireBodyConversion/RequirePhysics/RequireBoth`) + `Overhaul.Policy` init-default `Loose` (roadmap 5.3 target-body / patch demand); `MappingService` skeleton with the full API surface (`AssignDonor`/`AttachPatch`/`Unassign`/`DetachPatch`/`GetStatus`/`GetArmorSetStatus`/`GetOverhaulProgress`), `OverhaulProgress` DTO, `PatchKind`, and the empty/unmapped progress path implemented + tested (`Done` is a caller-side overlay, never a fifth derived status); `SyntheticCatalogUniverse` Iron set (Male + Female Heavy) + `MappingFixtures` (`CreateOverhaulWithCatalog`/`CreateDonorOutput`); full suite 460 tests green, Release 0 warnings/0 errors)
  - Sprint 3.1 `MappingService` CRUD + validation - done (`AssignDonor`/`AttachPatch`/`Unassign`/`DetachPatch` end-to-end on in-memory data; `UniqueKey` uniqueness (second assign replaces, never duplicates); `ValidateCrossProject` on every write (donor or patch from another project rejected with no partial state); patch-`Kind` checks (patch as main donor rejected, full-replacer/physics-as-body rejected); stale-instance safety in attach/detach (rebuild from the in-list mapping by Id); per-gender mapping with two donors; full suite 473 tests green, Release 0 warnings/0 errors)
  - Sprint 3.2 patch requirement detection + recommendation - done (`NeedFor` + `PatchRequirement` (`None/Body/Physics/Both`) over the COMBINED main-donor-OR-attached-patch Phase 2 flags + `Overhaul.Policy` - `Loose` no-demand, `RequireBodyConversion` demands body when the donor set has a body piece without a BodySlide flag or `BodyMarkerFromPath` marker, `RequirePhysics` demands physics when no physics flag; `GetStatus` -> `Pending` (unmapped)/`Mapped`/`NeedsPatch`; `BodyMarkerFromPath` body-token table (`3ba/3baf`->ThreeBA, `cbbe`->CBBE, `bhunp`->BHUNP, `himbo`->HIMBO, `unp/unpb`->BHUNP); `RecommendPatches` matching-`Kind` candidates sorted deterministically (`BodyConversionPatch` then `PhysicsPatch`, then `ImportId`); full suite 485 tests green, Release 0 warnings/0 errors)
  - Sprint 3.3 set status + Overhaul progress - done (`GetArmorSetStatus` per-set/per-gender `NotStarted/InProgress/Mapped/NeedsPatch` via target `PieceMapping`s - never `Done`, no done-override param; `SetDone` is the ONLY `Done` overlay toggle (records done only when the set is `Mapped`); `GetOverhaulProgress` counts a set `Done` exactly when `Mapped AND doneOverride`, with `DoneFraction` + `Remaining`; the five progress buckets are mutually exclusive and always sum to the total - recorded fix: the plan's four-bucket sum phrasing omitted the `Mapped` bucket; full suite 492 tests green, Release 0 warnings/0 errors)
  - Sprint 3.4 tests + real-donor spot-check + docs - done (consolidated mapping suite: `MappingServiceSkeletonTests` 3 + `MappingServiceCrudTests` 13 + `PatchDetectionTests` 12 + `SetStatusProgressTests` 7 + `MappingDeterminismTests` 2 - the same assign sequence over the same synthetic catalog/donor set yields identical statuses + progress every run regardless of freshly generated GUIDs; `RealDonorPatchDetectionIntegrationTests` (`[Trait("Category","Integration")]`, auto-skips without `ModsForTests/Armor`, cleans `%TEMP%/UW_Donor_Map_*`) classifies the real `Red Hood - HIMBO` (`BodyConversionPatch`, 2 BodySlide + 10 physics flags) and asserts a piece mapped to it reads `PatchRequirement.None` under `RequireBodyConversion`/`RequirePhysics` and `MappingStatus.Mapped` under `RequireBoth`, proving `NeedsPatch` reacts to the real classifier flags; full suite 495 tests green (481 non-integration + 14 Integration), Release 0 warnings/0 errors, no artifacts left)
- Phase 4 - SQLite persistence: done (Sprints 4.0-4.4)
  - Sprint 4.0 SQLite foundation - done (new `UltimateWardrobe.Persistence` project, `Microsoft.Data.Sqlite` 10.0, registered in slnx + Tests; `ProjectDatabase` open/create bootstrap - WAL journaling + `foreign_keys=ON` on every connection, `Pooling=False` so dispose really closes the file, empty-DB detection, typed `ProjectStoreException`; Persistence-local JSON layer `PersistenceJson` + `CatalogSourceJsonConverter` + row codecs; Core 4.0.2 additive amendment `Overhaul.CreatedAt` (init, default `UtcNow`) + `Overhaul.ModifiedAt` (init, nullable); new `ProjectDatabaseTests` + `SerializationRoundTripTests`; full suite 509 tests green, Release 0 warnings/0 errors, no artifacts left)
  - Sprint 4.1 migration engine + versioning + backup - done (`IMigration`/`IMigrator`/`Migrator`; `M001_Initial` creates the section 4.2 schema - SchemaVersion + Project/Overhaul/DonorAsset/PieceMapping/CatalogCache with FK + the `UNIQUE(OverhaulId, TargetPieceEditorId, TargetGender)` = `UniqueKey` + indexes - applied on `OpenAsync`: fresh DB -> `M001`, existing -> missing versions only each in its own transaction, newer DB -> fail-fast refuse-to-downgrade (`ProjectStoreException`); `.bak` backup (`project.db.bak`) before upgrading an existing schema, a failed backup aborts without touching the schema; corrupt DB -> typed `ProjectStoreException`; new `MigrationTests` covers fresh/idempotent/newer/upgrade+backup/corrupt/failed-backup; full suite 515 tests green, Release 0 warnings/0 errors, no artifacts left)
  - Sprint 4.2 repositories + transactions - done (`UnitOfWork` transaction boundary over the single `ProjectDatabase` connection with `BeginAsync` re-issuing `PRAGMA defer_foreign_keys=ON` per the 4.3.1 transaction-level note; `RowCodecs` for ISO-8601/enum-name/Guid conversion; the five repositories `ProjectRepository`/`OverhaulRepository` (SourceJson + Policy + timestamps)/`DonorAssetRepository` (JSON columns + Kind + explicit projectId)/`PieceMappingRepository` (ON CONFLICT on `UniqueKey` so a second assign replaces)/`CatalogCacheRepository` (whole-`Catalog` JSON per Overhaul); tests: CRUD + round-trip + upsert-replace collision + DB-level FK enforcement (a mapping to a donor not in the DB is rejected only because `foreign_keys=ON` is set, issue 3) + leaves-first delete ordering + transaction rollback/commit; new `RepositoryCrudTests` + `RepositoryMappingAndTransactionTests`; full suite 528 tests green, Release 0 warnings/0 errors, no artifacts left)
  - Sprint 4.3 IProjectStore facade + full-graph round-trip - done (`ProjectStore` implements the Phase 0.2 `IProjectStore`: `SaveAsync(Project)` persists the whole graph - Project row, `Library.Assets`, each Overhaul + its Mappings + per-overhaul `Catalog` cache - in ONE `UnitOfWork` transaction, upsert-only by stable domain id (issue 3, no delete-then-reinsert, rollback on failure); `LoadAsync(path)` opens + migrates and rebuilds the full graph, reconstructing each Overhaul (Catalog is `init`-only, set from `CatalogCache`); a DB with no Project row / unreadable file / newer schema surfaces as a typed `ProjectStoreException`; Core 4.3 additive amendment `Overhaul.Catalog` (init, nullable); tests: `ProjectStoreRoundTripTests` (deep-equality round-trip + `MappingService.GetArmorSetStatus`/`GetOverhaulProgress` parity + body-patch/detected-flags survive + double-save upsert-only idempotency) + `ProjectStoreLoadFailureTests` (missing/corrupt/newer-schema typed) + a pragma test proving `defer_foreign_keys=ON` is re-issued on every begin/commit cycle; full suite 535 tests green, Release 0 warnings/0 errors, no artifacts left)
  - Sprint 4.4 Integration + docs + DoD - done (new `ProjectStoreIntegrationTests` `[Trait("Category","Integration")]`: the full Phase 4 loop over a REAL donor - import + classify `Red Hood - HIMBO` (esp-less branch-2 fixture, `BodyConversionPatch` with real BodySlide + physics flags) -> synthesize the Iron catalog -> assign synthetic FullReplacer donors and ATTACH the real donor as the body-conversion patch layer through `MappingService` -> `SaveAsync` -> reopen in a fresh `ProjectDatabase` -> `LoadAsync` -> assert the reloaded graph deep-equals, `GetArmorSetStatus`/`GetOverhaulProgress` identical, and the extracted donor folder path still resolves; auto-skips when `ModsForTests/Armor` lacks the fixture, cleans `%TEMP%/UW_Donor_*` + test `project.db` dir in `finally`; `.gitignore` now blocks `*.db`, `*.db-wal`, `*.db-shm`, `*.db.bak` (via `*.bak`); full suite 536 tests green (521 non-integration + 15 Integration), Release 0 warnings/0 errors, no artifacts)

## Stack

- .NET 10 LTS (`net10.0-windows`), C# 13
- WPF (MVVM, CommunityToolkit.Mvvm) - from Phase 6
- Mutagen.Bethesda.Skyrim 0.54.4 - Phase 1 (Scanner): catalog scanning via Mutagen 0.54 - folder-only, masters-first order, ARMO -> ARMA -> files correlation, Outfit/EDID-mesh ArmorSet grouping, gender/weight variant assembly, catalog cache
- Mutagen.Bethesda.Skyrim 0.54.4 + M.E.Logging.Abstractions - Phase 2 (DonorLibrary): donor plugin probe + branch-1 classification over donor ARMO with reference-master enrichment, branch-2 mesh/texture heuristics for esp-less donors, branch-3 BodySlide/physics detection + `DonorAssetKind`, deterministic `ProvidedSets` in catalog shapes
- Phase 3 (Mapping) uses `Core` only (no package deps): manual mapping + patch detection + status/progress over the existing `PieceMapping`/`ArmorSetStatus`/`PatchPolicy` domain (Phase 3 done)
- SQLite + Microsoft.Data.Sqlite - Phase 4 (Persistence): one `project.db` per Project, WAL + `foreign_keys=ON`, forward-only migrations with `.bak` backup, repositories/`UnitOfWork`/`IProjectStore` whole-graph save/load
- Archives extracted natively via `7z.dll` (7z/zip) + `UnRAR64.dll` (rar), SharpCompress fallback - Phase 0.2

## Solution

```
UltimateWardrobe.slnx
├── src/UltimateWardrobe.Core        # Domain model, no I/O
├── src/UltimateWardrobe.Archives    # Archive extraction (native first)
├── src/UltimateWardrobe.Scanner     # Mutagen folder catalog scanner (Phase 1)
├── src/UltimateWardrobe.DonorLibrary# Donor import + classification (Phase 2, Sprints 2.0-2.5 done)
├── src/UltimateWardrobe.Mapping     # Manual mapping + patch detection + status/progress (Phase 3, Sprints 3.0-3.4 done)
├── src/UltimateWardrobe.Persistence # SQLite project DB (Phase 4 done, Sprints 4.0-4.4)
└── tests/UltimateWardrobe.Tests     # xUnit + FluentAssertions
```

Native binaries are under `runtimes/win-x64/native/` and copied to output as `Content`.

## Build

```powershell
dotnet --version  # must be 10.x
dotnet build -c Release
dotnet test
```

## Docs

- `Plans/final-roadmap.md` - full roadmap (Phases 0-7)
- `Plans/phase0.md`, `Plans/phase1.md`, `Plans/phase2.md`, `Plans/phase3.md`, `Plans/phase4.md` - implementation plans (phase 1, 2, 3, 4 done)
- `Docs/architecture.md` - architecture overview
- `Docs/domain-model.md` - domain model (Sprint 0.1 - done)
- `Docs/archive-layer.md` - archive layer (Sprint 0.2 - done)
- `Docs/scanner.md` - folder catalog scanner, grouping + gender/weight variants, catalog + cache, logging/report, committed goldens + integration gates (Sprint 1.7 - done)
- `Docs/donor-library.md` - donor import + graduated classification, plugin probe, branch-1 pipeline + reference-master merge, branch-2 mesh/texture heuristics, branch-3 BodySlide/physics + kind, `DonorLibraryService` import/remove/reclassify + cross-project guard, and the Sprint 2.5 test suite (synthetic goldens + real-donor integration) (Phase 2, Sprints 2.0-2.5 - done)
- `Docs/mapping.md` - manual mapping + patch detection + status/progress + determinism + real-donor spot-check (Phase 3, Sprints 3.0-3.4 - done)
- `Docs/persistence.md` - SQLite project DB: schema, migrations + backup, repositories/`IProjectStore`/`UnitOfWork` flow, JSON serialization, test strategy + Integration round-trip (Phase 4, Sprints 4.0-4.4 - done)

## Test Assets

Real mod archives for manual integration testing are under `ModsForTests/` (gitignored outputs, never committed). Small synthetic goldens are under `tests/TestData/Archives/`; scanner goldens under `tests/TestData/Plugins/` + `tests/TestData/CatalogGolden/` (committed, regenerated via `UW_WRITE_GOLDENS=1`).

Game ESMs for reference: `D:\Skymod\Stock Game` (read-only). Donor golden snapshots under `tests/TestData/DonorGolden/` (committed, regenerated via `UW_WRITE_GOLDENS=1`).
