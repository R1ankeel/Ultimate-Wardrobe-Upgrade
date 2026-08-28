# Patcher (Phase 5)

## Overview

`UltimateWardrobe.Patcher` implements the roadmap Section 7 Phase 5 workflow: it consumes the persisted Phase 4 graph (an `Overhaul` with its `Catalog`, `Mappings` and the project's `DonorLibrary`) and produces an MO2-ready replacer mod - an ESP with ARMA overrides plus the sliced donor `meshes`/`textures`/`SKSE`/`CalienteTools` files and a `meta.ini` - without touching load order or balance.

The full pipeline (resolution -> plugin writer -> file slicer -> orchestrator + `meta.ini`) lands across Sprints 5.0-5.4; this document describes what is implemented so far and the fixed contracts the remaining sprints build on.

## Architecture

- `Core/Abstractions/IPatcher.cs` (Sprint 5.0.2, additive) - `IPatcher.BuildAsync(Overhaul, DonorLibrary, outputDir, IProgress<PatchProgress>?, CancellationToken)` returning a `PatchResult`. New Core types: `PatchReport` (total/resolved/skipped mappings, overridden records, copied files + bytes, warnings), `PatchWarning` (message + optional context) and `PatchProgress` (stage + completed/total + optional detail). `PatchResult.Report` is `init`-only and `null` until the orchestrator attaches it; existing `PatchResult` constructor callers are unaffected.
- `PatchException` - the typed, build-blocking failure (for example an Overhaul with no catalog, or an unreadable source root). Per-mapping problems never throw; they are `PatchWarning`s.
- `TargetResolver` (Sprint 5.0.3) - see below.
- `PatchPathRules` (Sprint 5.1) - amendment #6 path rules shared with the Sprint 5.2 slicer: `Normalize` (backslash -> forward slash, trimmed, original case) for written paths, `EqualsNormalized` (backslash -> forward slash, ordinal case-insensitive, trimmed, BOTH sides) for the loose-path skip, and `ToGameRelative` (strips a leading `Data/` segment, "root-or-Data layout").
- `EffectiveMeshResult` + <see cref="PluginBuilder.ResolveEffectiveMesh"> (Sprint 5.1.2) - the amendment #8 decision: the effective mesh path (the normalized donor mesh path) plus the asset that physically owns the file; a body/physics patch manifest entry equal to the donor path shadows the donor, physics evaluated after body (last wins).
- `PluginBuilder` (Sprint 5.1) - see below.

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

## Test strategy

- `tests/UltimateWardrobe.Tests/Patcher/TargetResolverTests.cs` (Sprint 5.0.4): 10 unit tests over the synthetic mini universe (`SyntheticSkyrimMods.WriteMiniUniverse` - it already carries gender-split `Model` fixtures, so no fixture extension was needed) with a hand-built catalog (EditorID-primary, FormId fallback, `ArmaEditorId`-then-first-addon fallback, female-only / male-only model extraction) and negative paths (unknown target and unresolvable ARMA skip + warning; corrupt source plugin skips; missing catalog and missing game folder -> typed `PatchException`), plus one test resolving a mapping over a REAL scanned catalog (`FolderCatalogScanner` -> `Overhaul.Catalog` -> resolve), proving the resolver consumes the Phase 1 pipeline output as-is.
- `tests/UltimateWardrobe.Tests/Patcher/PluginBuilderTests.cs` (Sprint 5.1.5): 11 tests that write synthetic source plugins, build, and reopen the output with `SkyrimMod.CreateFromBinaryOverlay`. Assertions: gendered `WorldModel` writes, `UW_` prefix unique per addon, `OverriddenRecords` distinct-count, auto-collected masters, ESL flag only for the Vanilla + DLC source kind, body-patch shadowing (incl. a `Data/`-prefixed manifest), physics last-wins, and the amendment #6 loose-path skip (exact, normalized mixed-case backslash equality, null-current-never-skips).

## Remaining sprints (5.2-5.4, documented here as they land)

- 5.2 `FileSlicer` + `DonorFileLocator` - the whitelist slice (mesh + `_1st` + textures + BodySlide + physics xml + patch overlay, `last wins`, whole-export dedup).
- 5.3 `WardrobePatcher` orchestrator + `OutputFolder` - the `Export/<ModName>/` mod folder, clear-before-write re-export semantics and `meta.ini` with a `generated` UTC line.
- 5.4 Real-data Integration spot-check, xEdit validation proxy, DoD close-out.
- 5.2 `FileSlicer` + `DonorFileLocator` - the whitelist slice (mesh + `_1st` + textures + BodySlide + physics xml + patch overlay, `last wins`, whole-export dedup).
- 5.3 `WardrobePatcher` orchestrator + `OutputFolder` - the `Export/<ModName>/` mod folder, clear-before-write re-export semantics and `meta.ini` with a `generated` UTC line.
- 5.4 Real-data Integration spot-check, xEdit validation proxy, DoD close-out.