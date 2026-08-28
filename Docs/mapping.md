# Mapping: Manual Mapping + Patch Detection

> Phase 3, Sprint 3.0 scaffolding done - `src/UltimateWardrobe.Mapping` - manual mapping logic that binds a target `Piece` (from a Phase 1 `Catalog`) to a donor piece (from a Phase 2 `DonorAsset`) with optional body-conversion and physics patch layers, then derives `NeedsPatch`, per-set `ArmorSetStatus`, and Overhaul progress. Only `Core` is referenced (the `Catalog` and `DonorAsset` instances are passed in as arguments - no `Scanner`/`DonorLibrary` dependency). Sprints 3.1 (CRUD/validation), 3.2 (patch detection), 3.3 (status/progress), 3.4 (real-donor + docs) are pending in `Plans/phase3.md`.

## Overview

`Overhaul.Mappings` is the single source of truth for a project's mapping progress. A `PieceMapping` (Core, Phase 0.2) binds one target piece per gender to one donor piece and carries up to two patch layers:

```
- PieceMapping
    TargetArmorSetId, TargetPieceEditorId, TargetGender
    DonorAssetId, DonorPieceEditorId, DonorMeshPath
    BodyConversionPatchAssetId?  PhysicsPatchAssetId?
    Status (MappingStatus)
```

The mapping is keyed by `PieceMapping.UniqueKey` = `$"{OverhaulId}:{TargetPieceEditorId}:{TargetGender}"` - one active mapping per target piece per gender. `MappingService` (Sprint 3.0 skeleton; full API in Sprints 3.1-3.3) is logic only, no I/O: the caller owns the `Overhaul`, the project's `DonorLibrary`, and the `Catalog`.

## Core amendment (Sprint 3.0.2)

`PatchPolicy` enum (`Loose / RequireBodyConversion / RequirePhysics / RequireBoth`) plus `Overhaul.Policy` (init-only, default `Loose`) - the machine-readable form of roadmap 5.3 "the user chose 3BA/HIMBO as the target body / wants physics". `Loose` drives `NeedsPatch` purely from the donor's (or its attached patch's) `Detected*` flags; the `Require*` values additionally demand the missing layer.

## Components (Sprint 3.0)

| File | Purpose |
|------|---------|
| `MappingService.cs` | The mapping API (Sprint 3.0 skeleton): `AssignDonor` / `AttachPatch` / `Unassign` / `DetachPatch` (3.1), `GetStatus` (3.2), `GetArmorSetStatus` / `GetOverhaulProgress` (3.3). Currently the CRUD and full derivation throw `NotImplementedException`; the empty / unmapped progress path is implemented and tested. |
| `OverhaulProgress.cs` | Overhaul-level progress DTO (total, per-status counts, done fraction, remaining). |
| `PatchKind.cs` | `Body / Physics` - which patch layer an attach/detach call targets. |
| `tests/.../Mapping/SyntheticCatalogUniverse.cs` | Runtime-synthesized Iron catalog (Male + Female Heavy variants, cuirass + gauntlets each) - no files on disk. |
| `tests/.../Mapping/MappingFixtures.cs` | `CreateOverhaulWithCatalog` + `CreateDonorOutput`/`CreateIronDonor` donor builders. |

## MappingService API

- `AssignDonor(overhaul, catalog, donorAsset, targetPiece, donorPiece)` - create/set a `PieceMapping` (replaces by `UniqueKey`), resolve `DonorMeshPath`, re-derive status, `ValidateCrossProject`. (Sprint 3.1)
- `AttachPatch(mapping, patchAsset, PatchKind)` - set the body or physics layer, requiring `patchAsset.Kind` to match (3.1).
- `Unassign(mapping)` / `DetachPatch(mapping, PatchKind)` - remove a mapping / clear one layer (3.1).
- `GetStatus(mapping, donorAsset, patchAssetBody?, patchAssetPhysics?, policy) -> MappingStatus` (3.2).
- `GetArmorSetStatus(catalogSet, mappings) -> ArmorSetStatus` - only the four stable values `NotStarted / InProgress / Mapped / NeedsPatch`; NEVER returns `Done` and takes no done-override (3.3).
- `GetOverhaulProgress(mappings, catalog, doneOverrides) -> OverhaulProgress` (3.3).

## Done is an overlay, not a fifth status

`Done` is a caller-side boolean overlay (`doneOverrides`), combined only by `GetOverhaulProgress`:

```
user-facing set state = Done          if  GetArmorSetStatus(...) == Mapped  AND  doneOverrides[setId] == true
                      = GetArmorSetStatus(...)   otherwise
```

The invariant `Done + InProgress + NeedsPatch + NotStarted == TotalSets` always holds. The Phase 5 export gate reads the combined `Mapped AND doneOverride`, never a bare `Status == Done`.

## Patch detection (Sprint 3.2)

`NeedFor(mapping, donorAsset, patchAssetBody?, patchAssetPhysics?, policy)` returns the missing layer(s). Flags are the **combined** flags of the main donor OR the attached per-layer patch asset:

- Body layer satisfied: main donor `DetectedBodySlideFiles` non-empty OR the attached body patch's `DetectedBodySlideFiles` non-empty.
- Physics layer satisfied: main donor `DetectedPhysicsFiles` non-empty OR the attached physics patch's `DetectedPhysicsFiles` non-empty.
- `Loose`: the OR above is the whole rule.
- `RequireBodyConversion`: additionally demands the body layer when the donor set has a body piece and neither a BodySlide flag nor an explicit body-type marker in the donor mesh path is present.
- `RequirePhysics`: demands the physics layer when no physics flags are present.

`BodyMarkerFromPath(donorMeshPath)` maps path tokens (case-insensitive) to `BodyType`: `3ba` -> ThreeBA (also `3baf`), `cbbe` -> CBBE, `bhunp` -> BHUNP, `himbo` -> HIMBO, `unp|unpb` -> BHUNP marker, `no token` -> null. It is the explicit, path-only form of roadmap 5.3 "mesh path implies the target body" - gated by the Sprint 3.4 real-donor spot-check and reducible to "BodySlide flag only" (a one-line table edit) if no real donor needs it.

Patch recommendations: `RecommendPatches(donorLibrary, requirement)` returns candidate `DonorAsset`s of the matching `Kind`, sorted deterministically (`BodyConversionPatch` before `PhysicsPatch`, then by `ImportId`).

## Status derivation (Sprint 3.3)

```
MappingStatus: not assigned -> Pending; required layer missing -> NeedsPatch; else -> Mapped

ArmorSetStatus (per set, per gender):
  no piece mapped      -> NotStarted
  any piece NeedsPatch -> NeedsPatch
  every piece mapped   -> Mapped
  else                 -> InProgress
```

## Testing Strategy

- Framework: xUnit + FluentAssertions (same as the rest of the suite); `dotnet test` is the only runner.
- No external data for `Category!=Integration`: the `Catalog` is `SyntheticCatalogUniverse` and donors are `DonorAsset` fixtures - the layer is pure over in-memory `Core` types.
- Determinism: the service is a pure function of its inputs - same catalog + donor set + assign sequence yields the same statuses/progress.
- Sprints 3.1-3.3 add the CRUD/validation, patch-detection, and status/progress suites; Sprint 3.4 adds the real-donor Integration spot-check (auto-skips without `ModsForTests/Armor`).
- Current count (Sprint 3.0): 3 skeleton tests + 2 `Overhaul.Policy` tests; full suite 460 passing, 0 warnings / 0 errors on Release build.
