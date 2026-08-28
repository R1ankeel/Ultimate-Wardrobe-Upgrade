# Patcher (Phase 5)

## Overview

`UltimateWardrobe.Patcher` implements the roadmap Section 7 Phase 5 workflow: it consumes the persisted Phase 4 graph (an `Overhaul` with its `Catalog`, `Mappings` and the project's `DonorLibrary`) and produces an MO2-ready replacer mod - an ESP with ARMA overrides plus the sliced donor `meshes`/`textures`/`SKSE`/`CalienteTools` files and a `meta.ini` - without touching load order or balance.

The full pipeline (resolution -> plugin writer -> file slicer -> orchestrator + `meta.ini`) lands across Sprints 5.0-5.4; this document describes what is implemented so far and the fixed contracts the remaining sprints build on.

## Architecture

- `Core/Abstractions/IPatcher.cs` (Sprint 5.0.2, additive) - `IPatcher.BuildAsync(Overhaul, DonorLibrary, outputDir, IProgress<PatchProgress>?, CancellationToken)` returning a `PatchResult`. New Core types: `PatchReport` (total/resolved/skipped mappings, overridden records, copied files + bytes, warnings), `PatchWarning` (message + optional context) and `PatchProgress` (stage + completed/total + optional detail). `PatchResult.Report` is `init`-only and `null` until the orchestrator attaches it; existing `PatchResult` constructor callers are unaffected.
- `PatchException` - the typed, build-blocking failure (for example an Overhaul with no catalog, or an unreadable source root). Per-mapping problems never throw; they are `PatchWarning`s.
- `TargetResolver` (Sprint 5.0.3) - see below.

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

## Test strategy

- `tests/UltimateWardrobe.Tests/Patcher/TargetResolverTests.cs` (Sprint 5.0.4): 10 unit tests over the synthetic mini universe (`SyntheticSkyrimMods.WriteMiniUniverse` - it already carries gender-split `Model` fixtures, so no fixture extension was needed) with a hand-built catalog (EditorID-primary, FormId fallback, `ArmaEditorId`-then-first-addon fallback, female-only / male-only model extraction) and negative paths (unknown target and unresolvable ARMA skip + warning; corrupt source plugin skips; missing catalog and missing game folder -> typed `PatchException`), plus one test resolving a mapping over a REAL scanned catalog (`FolderCatalogScanner` -> `Overhaul.Catalog` -> resolve), proving the resolver consumes the Phase 1 pipeline output as-is.

## Remaining sprints (5.1-5.4, documented here as they land)

- 5.1 `PluginBuilder` - the output ESP with ARMA override records (`GetOrAddAsOverride`), gendered `WorldModel` writes, `UW_` EditorID prefix, auto masters, the source-kind-driven ESL gate and the amendment #6 loose-path skip.
- 5.2 `FileSlicer` + `DonorFileLocator` - the whitelist slice (mesh + `_1st` + textures + BodySlide + physics xml + patch overlay, `last wins`, whole-export dedup).
- 5.3 `WardrobePatcher` orchestrator + `OutputFolder` - the `Export/<ModName>/` mod folder, clear-before-write re-export semantics and `meta.ini` with a `generated` UTC line.
- 5.4 Real-data Integration spot-check, xEdit validation proxy, DoD close-out.