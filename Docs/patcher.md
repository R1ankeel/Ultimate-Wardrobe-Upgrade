# Patcher (Phase 5)

## Overview

`UltimateWardrobe.Patcher` implements the roadmap Section 7 Phase 5 workflow: it consumes the persisted Phase 4 graph (an `Overhaul` with its `Catalog`, `Mappings` and the project's `DonorLibrary`) and produces an MO2-ready replacer mod - an ESP with ARMA overrides plus the sliced donor `meshes`/`textures`/`SKSE`/`CalienteTools` files and a `meta.ini` - without touching load order or balance.

The full pipeline (resolution -> plugin writer -> file slicer -> orchestrator + `meta.ini`) lands across Sprints 5.0-5.4; this document describes the shipped product and the fixed contracts that governed the sprints.

## Architecture

- `Core/Abstractions/IPatcher.cs` (Sprint 5.0.2, additive) - `IPatcher.BuildAsync(Overhaul, DonorLibrary, outputDir, IProgress<PatchProgress>?, CancellationToken)` returning a `PatchResult`. New Core types: `PatchReport` (total/resolved/skipped mappings, overridden records, copied files + bytes, warnings), `PatchWarning` (message + optional context) and `PatchProgress` (stage + completed/total + optional detail). `PatchResult.Report` is `init`-only and `null` until the orchestrator attaches it; existing `PatchResult` constructor callers are unaffected.
- `PatchException` - the typed, build-blocking failure (for example an Overhaul with no catalog, or an unreadable source root). Per-mapping problems never throw; they are `PatchWarning`s.
- `TargetResolver` (Sprint 5.0.3) - see below.
- `PatchPathRules` (Sprint 5.1) - amendment #6 path rules shared with the Sprint 5.2 slicer: `Normalize` (backslash -> forward slash, trimmed, original case) for written paths, `EqualsNormalized` (backslash -> forward slash, ordinal case-insensitive, trimmed, BOTH sides) for the loose-path skip, and `ToGameRelative` (strips a leading `Data/` segment, "root-or-Data layout").
- `EffectiveMeshResult` + <see cref="PluginBuilder.ResolveEffectiveMesh"> (Sprint 5.1.2) - the amendment #8 decision: the effective mesh path (the normalized donor mesh path) plus the asset that physically owns the file; a body/physics patch manifest entry equal to the donor path shadows the donor, physics evaluated after body (last wins).
- `PluginBuilder` (Sprint 5.1) - see below.
- `DonorFileLocator` (Sprint 5.2.1) - game-relative path -> physical file under an extracted donor folder: root first, then `Data/`; a named traversal guard rejects `..`/`.`/empty segments and re-verifies the full path stays under the root; missing -> `null` (the caller records the warning, never an exception).
- `FileSlicer` (Sprint 5.2) - see below.
- `OutputFolder` (Sprint 5.3.1) - the plan section 4.6 mod-folder layout and `meta.ini` writer, see below.
- `WardrobePatcher` (Sprint 5.3.2) - the `IPatcher` orchestrator, see below.

`UltimateWardrobe.Patcher` references `UltimateWardrobe.Core` + `UltimateWardrobe.Scanner` per the plan's amendment #4 (the Scanner stays the single place that reaches Mutagen) and `Microsoft.Extensions.Logging.Abstractions`. It is registered in `UltimateWardrobe.slnx` and the Tests project.

## Target resolution (Sprint 5.0)

`TargetResolver.Resolve(Overhaul, CancellationToken)` maps each `PieceMapping` to a live `(IArmorGetter, IArmorAddonGetter)` target pair:

1. Guard: `Overhaul.Catalog` is required (amendment #5). Without it the resolver throws a typed `PatchException`.
2. Load the source over the Phase 1 pipeline, replaying `PluginDiscovery.Discover(catalog.Source)` -> `LoadOrderBuilder.Build` -> `ModLoader.TryLoad` (per-plugin failures warn and skip) -> `RecordIndex.Build` (Scanner reuse, amendment #4), plus an EditorID -> record index built in load order for both ARMO and ARMA.
3. Per mapping:
   - locate the catalog piece by (set id, variant gender, piece EditorId);
   - resolve the ARMO by EditorID (primary) with a FormId fallback across the loaded mod keys (`FormKey(modKey, piece.FormId)`);
   - resolve the ARMA by `Piece.ArmaEditorId` then, as fallback, the ARMO's first resolvable armature addon;
   - extract the target ARMA's current `Model` file paths per gender, normalized to forward slashes (matching the Phase 1 `GivenPath` normalization) for the amendment #6 loose-path skip.

The result is a deterministic `TargetResolutionResult`: `ResolvedTarget`s in mapping order (`Mapping`, `ArmorKey`, `ArmorAddonKey`, gendered current-model paths) plus the accumulated warnings converted to `PatchWarning`s. Loaded overlays are disposed in `finally`; `ResolvedTarget` carries only copied data, never live getters.

Failures: missing catalog / unreadable source root -> `PatchException`; an unresolved mapping (piece absent, ARMO or ARMA not found) -> `PatchWarning` + skip, the build continues. Cancellation is checked between plugins and between mappings.

## Plugin builder (Sprint 5.1)

`PluginBuilder.Build(Overhaul, IReadOnlyList<ResolvedTarget>, DonorLibrary, string outputDir, CancellationToken)` writes the output ESP and returns a `PatchResult` with `PluginPath`:

1. Drop-in: the output is always a fixed new mod, never an override of the source - `ModKey.FromFileName($"UltimateWardrobe - {overhaul.Name}.esp")`, `SkyrimRelease.SkyrimSE`.
2. ESL gate (amendment #7): when the catalog source kind is Vanilla + DLC the plugin is marked `((IMod)output).IsSmallMaster = true`; story-mod sources keep a full plugin. The flag survives the `WriteToBinary` -> `CreateFromBinaryOverlay` round trip.
3. The builder re-loads the source over the Phase 1 pipeline (plugin discovery, load order, mod loader, record index) and keeps the loaded overlays alive in a private container while the overrides are written - the getters must outlive the `GetOrAddAsOverride` calls; disposed in `finally`.
4. Per resolved target:
   - the target ARMA getter is re-resolved by `ArmorAddonKey` (FormKey) and inserted via `ArmorAddons.GetOrAddAsOverride` (mixin in namespace `Mutagen.Bethesda`);
   - the effective mesh is decided per amendment #8 (`ResolveEffectiveMesh`): the normalized donor mesh path unless a body/physics patch manifest entry shadows it (body first, physics last wins; path equality is game-relative, `Data/`-prefix-insensitive);
   - the effective path is `ToGameRelative` (a leading `Data/` segment is stripped) and written with forward slashes into the gendered `WorldModel` slot; a missing gender slot is created via `GenderedItem<Model?>(null, null)`, never dereferenced;
   - the EditorID is prefixed `UW_` (ordinal-guarded so a second mapping over the same addon never double-prefixes);
   - amendment #6 loose-path skip: when the effective mesh equals the target ARMA's current normalized model path, no override record is written - the loose file already wins - and a skip warning is recorded; a null current path never equals anything, so the override IS written.
5. `WriteToBinary` collects masters automatically; `OverriddenRecords` counts distinct ARMA FormKeys actually inserted; source-load and skip warnings accumulate into the attached `PatchReport`. The `DonorLibrary` (~> `EffectiveMeshResult`) is resolved at mapping level so the same mesh decision that writes the only loose file reaches the record (amendment #8 pair rule).

## File slicer (Sprint 5.2)

`DonorFileLocator.TryLocate(string? gameRelativePath)` maps a game-relative path (e.g. `meshes/armor/iron/m/cuirass.nif`) to a physical file inside an extracted donor folder, trying `<ExtractedPath>/<rel>` then `<ExtractedPath>/Data/<rel>` (the classifier's root-or-`Data/` layout). A traversal guard rejects any `..`, `.` or empty segment and re-verifies that the fully-resolved path stays under the extracted root, so a crafted manifest/mapping path can never make the slicer read outside the donor folder. A missing file returns `null`; the caller records a `PatchWarning`, never an exception.

`FileSlicer.Slice(IReadOnlyList<PieceMapping>, DonorLibrary, string outputDir, CancellationToken)` selects exactly the whitelisted set per mapping and copies it with the game-relative path preserved, returning a `SliceResult` (`CopiedFiles` distinct + ordinal, `CopiedBytes`, `SkippedMappings`, `Warnings`):

1. Primary mesh - the amendment #8 effective mesh path (decided by an internal `PluginBuilder.ResolveEffectiveMesh`, so the plugin record and the loose slice always agree), located from the mesh-provider asset (the donor, the body patch or the physics patch - last wins). A missing primary mesh skips the whole mapping with a warning; nothing partial is ever copied for it.
2. `_1st`/`_1stperson` alternates - donor-manifest entries in the same meshes folder whose stem collapses to the piece token (`MatchStem` strips `_1stperson`/`_1st`/`_0`/`_1`, implemented locally under amendment #4). A `_0`/`_1` weight variant with no `_1st` marker is intentionally NOT sliced (the plan whitelist ships the primary + first-person alternates only).
3. Textures - the provided-set `Piece.TexturePaths` matched by `DonorPieceEditorId` (ordinal); when empty, the folder-mirror fallback: every `textures/**/*.dds` manifest entry whose directory mirrors the mesh folder's `meshes/` tail.
4. BodySlide - `DetectedBodySlideFiles` whose file name contains the donor piece token or the alphanumeric-lowercase target-set token.
5. Physics - when a physics patch is attached, that patch manifest's `SKSE/Plugins/**` entries are sliced and the donor's own detected physics is suppressed; otherwise the donor's matching `DetectedPhysicsFiles`.
6. Patch overlays, body first then physics (last wins): a body/physics patch manifest entry is taken when it collides with an already-sliced output path, lives under `CalienteTools/BodySlide/**` or `SKSE/Plugins/**`, or mirrors the effective mesh or one of its `_1st` alternates. Patch junk (e.g. a `Docs/readme.txt` next to the patch meshes) is excluded.

The whole export is de-duplicated per output-relative path (one `SortedDictionary`; overwrite semantics = "last wins" across mappings too), then copied once in deterministic ordinal order. Cancellation is honored between mappings and between copies. A physical copy failure (e.g. a locked destination) surfaces as a typed `PatchException`; every per-file problem short of that is a `PatchWarning`. The output folder is only created when the first file is copied.

## Orchestrator + output folder (Sprint 5.3)

`OutputFolder` is the plan section 4.6 layout. `ModName(overhaulName)` is `UltimateWardrobe - <Name>` with the name sanitized for the file system (illegal Windows file-name characters become `_`, trailing dots/spaces are trimmed, an all-illegal result falls back to `Unnamed`); `PluginFileName` is that plus `.esp`. `ResolveModDir(outputDir, name)` returns the full path `<outputDir>/<ModName>` and verifies it is strictly below the output directory - an output path that is a file, or a mod dir resolving to the output directory itself or above it, throws a typed `PatchException` BEFORE anything is touched (the clean-before-write step must never delete the output directory or anything above it). `ClearModDir(modDir)` rebuilds the folder empty (delete-then-rebuild), so a re-export can never leave stale or orphaned files behind; a folder that cannot be cleared (e.g. content locked by another process) is also a typed `PatchException`. `WriteMetaIni(modDir, name, mappedSets, generatedUtc?)` writes the section 4.6 layout - `[General]`, `name=UltimateWardrobe - <Name>`, `version=1.0.0`, `category=Armor Replacer`, `notes=Generated by UltimateWardrobe on <UTC>. Overhaul: <Name>, <N> sets mapped.` and a `generated=<yyyy-MM-dd HH:mm:ss UTC>` stamp. The stamp is UTC at second granularity and defaults to `DateTime.UtcNow`, so consecutive exports always differ (locked by an explicit two-timestamp unit test).

`WardrobePatcher` implements `IPatcher.BuildAsync(Overhaul, DonorLibrary, outputDir, IProgress<PatchProgress>?, CancellationToken)` with the full export loop:

1. Resolve the mappings (`TargetResolver.Resolve`) - a missing `Overhaul.Catalog` throws a typed `PatchException` BEFORE any output path is touched.
2. `ResolveModDir` + `ClearModDir` - the export folder is rebuilt empty before any write.
3. `PluginBuilder.Build` writes the esp into the mod folder.
4. `FileSlicer.Slice` runs over the RESOLVED mappings (the resolver's `ResolvedTarget.Mapping` list) so a skipped mapping is never double-counted.
5. `WriteMetaIni` with the mapped-set count = distinct `TargetArmorSetId`s among the resolved targets.
6. The composed `PatchReport` is attached to the `PatchResult` (`PluginPath`, `CopiedFiles`): `TotalMappings` = the overhaul's full mapping count, `ResolvedMappings` = resolved count, `SkippedMappings` = (total - resolved) + the slicer's own skips, `OverriddenRecords` from the plugin build, `CopiedFiles`/`CopiedBytes` from the slice, `Warnings` = resolution + plugin + slice warnings concatenated. Coarse `PatchProgress` is reported per stage (Resolve targets -> Prepare export folder -> Build esp plugin -> Copy donor files -> Write meta.ini; completed/total = stage/5).
7. Everything is synchronous over deterministic inputs, so two runs over the same graph produce byte-identical esp output and an identical report (locked by a two-run test).

## Real-data Integration spot-check (Sprint 5.4)

`PatcherRealDataIntegrationTests.RealDonor_RealGame_GeneratesMutagenValidModFolder` is an opt-in Integration test (auto-skips when the game root or the donor fixture is absent) that drives the WHOLE real pipeline once: it imports the real `Red Hood - Main File` archive (`ModsForTests/Armor`, branch-1 FullReplacer, 65 files), classifies it, scans the real `D:\Skymod\Stock Game` catalog (`VanillaCatalogSource`, 4197 ARMO records grouped into 3396 sets, 0 warnings), picks the real Iron set via EditorID, maps one real mesh, and runs `WardrobePatcher.BuildAsync` into a temp mod folder. It then re-opens the output esp with Mutagen (`CreateFromBinaryOverlay`) and asserts: the ESL (small-master) flag, the master set is EXACTLY `[Skyrim.esm]` (the master-consistency proxy), the single `UW_` override wrote the donor mesh path into the gendered `WorldModel`, that game-relative path physically resolves back inside the extracted donor (root-or-Data layout), the output tree is exactly the expected sliced files + esp + `meta.ini` (nothing else), the report numbers (1 mapping resolved, 0 skipped, 1 overridden record) and the `meta.ini` fields; the temp folders are removed in `finally`. The donor spot-check recorded two real-data facts: the real Iron ARMO EditorIDs carry the `Armor` prefix, and the Red Hood esp stores its piece meshes WITHOUT the `meshes/` prefix (`armor/ZerofrostRedHood/...`), so the mapping targets a physically-present archive mesh instead.

## Sprint 5.4 notes

The manual xEdit hand-check is a one-off validation on the working machine (it is a GUI tool, not a CI gate). Its result is recorded in `Plans/phase5.md` alongside this sprint's Done note.

## Future improvements

- BSA packing: as a later-phase candidate, the copied loose files could be packed into `.bsa` archives via `Mutagen.Bethesda.Archive`, shrinking the mod folder and improving launch performance; the current loose-file layout is the correct default because it is tooling-agnostic (works with the game's default `Archive` setting) and trivially diffable.

## Test strategy

- `tests/UltimateWardrobe.Tests/Patcher/TargetResolverTests.cs` (Sprint 5.0.4): 10 unit tests over the synthetic mini universe (`SyntheticSkyrimMods.WriteMiniUniverse` - it already carries gender-split `Model` fixtures, so no fixture extension was needed) with a hand-built catalog (EditorID-primary, FormId fallback, `ArmaEditorId`-then-first-addon fallback, female-only / male-only model extraction) and negative paths (unknown target and unresolvable ARMA skip + warning; corrupt source plugin skips; missing catalog and missing game folder -> typed `PatchException`), plus one test resolving a mapping over a REAL scanned catalog (`FolderCatalogScanner` -> `Overhaul.Catalog` -> resolve), proving the resolver consumes the Phase 1 pipeline output as-is.
- `tests/UltimateWardrobe.Tests/Patcher/PluginBuilderTests.cs` (Sprint 5.1.5): 11 tests that write synthetic source plugins, build, and reopen the output with `SkyrimMod.CreateFromBinaryOverlay`. Assertions: gendered `WorldModel` writes, `UW_` prefix unique per addon, `OverriddenRecords` distinct-count, auto-collected masters, ESL flag only for the Vanilla + DLC source kind, body-patch shadowing (incl. a `Data/`-prefixed manifest), physics last-wins, and the amendment #6 loose-path skip (exact, normalized mixed-case backslash equality, null-current-never-skips).
- `tests/UltimateWardrobe.Tests/Patcher/FileSlicerTests.cs` (Sprint 5.2.3): 13 tests over runtime-synthesized donor folders. Whitelist assertions: the exact output set (effective mesh + `_1st`/`_1stperson` alternates + provided-set textures or the folder-mirror fallback + matching BodySlide + physics from donor or patch) with `_0` weight variants, stray siblings and junk excluded; whole-export dedup (one texture shared by two mappings copied once); the body-then-physics overlay (last wins on every colliding path, patch-only `CalienteTools/BodySlide/**` + `SKSE/Plugins/**` copied, `Docs/readme.txt` junk excluded); physics-patch-attached replacing the donor's detected physics; missing-primary-mesh -> skipped mapping + warning with no partial output; missing-texture -> warning only (the mesh still copies); traversal rejection (locator direct + a `../` mesh probe skipping the mapping); root-vs-`Data/` layout parity (locator resolution + identical output trees from two donors); pre-cancelled token -> `OperationCanceledException`; non-mapped mapping produces nothing and never creates the output folder.
- `tests/UltimateWardrobe.Tests/Patcher/OutputFolderTests.cs` (Sprint 5.3.3): 11 unit tests. The `ModName` sanitizer (illegal-char replacement, trailing dot/space trimming, all-illegal fallback `Unnamed`, blank -> `ArgumentException`); `PluginFileName`; `ResolveModDir` resolving strictly under the output dir and throwing a typed `PatchException` for a file-as-output; `ClearModDir` delete-then-rebuild clearing stale content and throwing a typed `PatchException` for locked content; `WriteMetaIni` exact section 4.6 layout and the two-timestamp test proving the `generated` stamp differs between runs (second-granularity UTC).
- `tests/UltimateWardrobe.Tests/Patcher/WardrobePatcherTests.cs` (Sprint 5.3.3): 5 end-to-end tests over the synthetic mini universe - a 2-mapping `Overhaul` on two target sets with donor BodySlide/physics + a BodyConversion patch + a PhysicsPatch overlay, run through a real `WardrobePatcher.BuildAsync` (with `PatchProgress` capture). The happy path asserts: `PluginPath`, the exact 13-file whitelisted slice + the full 15-entry mod-folder tree (sliced files + esp + `meta.ini`, nothing else), the `PatchReport` numbers, the reopened esp (UW_ prefix, gendered `WorldModel` paths incl. the physics-patch-provided helmet mesh, ESL flag, auto masters), the `meta.ini` section 4.6 fields incl. the `generated=... UTC` regex, and the 5 coarse progress stages. Two-run determinism locks byte-identical esp + identical report; re-export cleanliness plants an orphan `meshes/orphan/ghost.nif`, an orphan `.bsa`, a stray file and a stale `meta.ini` (with a `stalekey` field) and asserts the second run's tree is again exactly the whitelist with the esp byte-identical and no stale fields; a locked export folder and a missing catalog each surface as a typed `PatchException`.

- `tests/UltimateWardrobe.Tests/Patcher/PatcherRealDataIntegrationTests.cs` (Sprint 5.4.1): 1 Integration test (`[Trait("Category","Integration")]`) over the real donor fixture + the real game catalog, as described above - ESP re-open + master-consistency + physical donor-file resolution + exact output tree + report/meta.ini assertions, auto-skip when the fixtures are absent.

## Remaining sprints

- None; Phase 5 ships with Sprint 5.4. Future work is tracked in `Docs/domain-model.md` (Phase 6 WPF) and the Future improvements note above.