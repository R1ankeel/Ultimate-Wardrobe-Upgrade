# Mapping: Manual Mapping + Patch Detection

> Phase 3, Sprints 3.0-3.3 done - `src/UltimateWardrobe.Mapping` - manual mapping logic that binds a target `Piece` (from a Phase 1 `Catalog`) to a donor piece (from a Phase 2 `DonorAsset`) with optional body-conversion and physics patch layers, then derives `NeedsPatch`, per-set `ArmorSetStatus`, and Overhaul progress. Only `Core` is referenced (the `Catalog` and `DonorAsset` instances are passed in as arguments - no `Scanner`/`DonorLibrary` dependency). Sprint 3.4 (real-donor spot-check + docs) is pending in `Plans/phase3.md`.

## Overview

`Overhaul.Mappings` is the single source of truth for a project's mapping progress. A `PieceMapping` (Core, Phase 0.2) binds one target piece per gender to one donor piece and carries up to two patch layers:

```
- PieceMapping
    TargetArmorSetId, TargetPieceEditorId, TargetGender
    DonorAssetId, DonorPieceEditorId, DonorMeshPath
    BodyConversionPatchAssetId?  PhysicsPatchAssetId?
    Status (MappingStatus)
```

The mapping is keyed by `PieceMapping.UniqueKey` = `$"{OverhaulId}:{TargetPieceEditorId}:{TargetGender}"` - one active mapping per target piece per gender. `MappingService` (Sprints 3.0-3.1) is logic only, no I/O: the caller owns the `Overhaul`, the project's `DonorLibrary`, and the `Catalog`.

## Core amendment (Sprint 3.0.2)

`PatchPolicy` enum (`Loose / RequireBodyConversion / RequirePhysics / RequireBoth`) plus `Overhaul.Policy` (init-only, default `Loose`) - the machine-readable form of roadmap 5.3 "the user chose 3BA/HIMBO as the target body / wants physics". `Loose` drives `NeedsPatch` purely from the donor's (or its attached patch's) `Detected*` flags; the `Require*` values additionally demand the missing layer.

## Components (Sprint 3.0)

| File | Purpose |
|------|---------|
| `MappingService.cs` | The mapping API: `AssignDonor` / `AttachPatch` / `Unassign` / `DetachPatch` (3.1), `GetStatus` / `NeedFor` / `BodyMarkerFromPath` / `RecommendPatches` (3.2), `GetArmorSetStatus` / `SetDone` / `GetOverhaulProgress` (3.3) - all implemented and tested. It is constructed for one project's `DonorLibrary` (used by `ValidateCrossProject`). |
| `PatchRequirement.cs` | `None / Body / Physics / Both` - the missing patch layer(s) feeding `GetStatus` and `RecommendPatches` (Sprint 3.2). |
| `OverhaulProgress.cs` | Overhaul-level progress DTO (total, per-status counts, done fraction, remaining). |
| `PatchKind.cs` | `Body / Physics` - which patch layer an attach/detach call targets. |
| `tests/.../Mapping/SyntheticCatalogUniverse.cs` | Runtime-synthesized Iron catalog (Male + Female Heavy variants, cuirass + gauntlets each) - no files on disk. |
| `tests/.../Mapping/MappingFixtures.cs` | `CreateOverhaulWithCatalog` + `CreateDonorOutput`/`CreateIronDonor` donor builders. |

## MappingService API

- `AssignDonor(overhaul, catalog, donorAsset, targetPiece, donorPiece)` - create/set a `PieceMapping` (replaces by `UniqueKey`), resolve `DonorMeshPath`, re-derive status, `ValidateCrossProject`. (Sprint 3.1, implemented)
- `AttachPatch(overhaul, mapping, patchAsset, PatchKind)` - set the body or physics layer, requiring `patchAsset.Kind` to match (3.1, implemented).
- `Unassign(overhaul, mapping)` / `DetachPatch(overhaul, mapping, PatchKind)` - remove a mapping / clear one layer (3.1, implemented).
- `GetStatus(mapping, donorAsset, patchAssetBody?, patchAssetPhysics?, policy) -> MappingStatus` - `Pending` (null mapping) / `Mapped` / `NeedsPatch`, derived from `NeedFor`. (3.2, implemented)
- `NeedFor(mapping, donorAsset, patchAssetBody?, patchAssetPhysics?, policy) -> PatchRequirement` - pure missing-layer detection. (3.2, implemented)
- `BodyMarkerFromPath(donorMeshPath) -> BodyType?` - path-only body-token marker. (3.2, implemented)
- `RecommendPatches(donorLibrary, requirement) -> IReadOnlyList<DonorAsset>` - candidates by matching `Kind`, deterministic order. (3.2, implemented)
- `GetArmorSetStatus(catalogSet, mappings) -> ArmorSetStatus` - per-set, per-gender derivation over the set's pieces x variants; only the four stable values `NotStarted / InProgress / Mapped / NeedsPatch`; NEVER returns `Done` and takes no done-override. (3.3, implemented)
- `SetDone(catalogSet, mappings, bool) -> IReadOnlyDictionary<string,bool>` - the ONLY way to toggle the caller-side `Done` overlay; returns the overlay state for the set (recorded as done only when the set is currently `Mapped`). (3.3, implemented)
- `GetOverhaulProgress(mappings, catalog, doneOverrides) -> OverhaulProgress` - per-status counts + done fraction; a set counts as `Done` exactly when `GetArmorSetStatus == Mapped AND doneOverrides[setId] == true`. (3.3, implemented)

## CRUD + validation (Sprint 3.1)

- **UniqueKey uniqueness** - `AssignDonor` replaces any mapping with the same `TargetPieceEditorId + TargetGender`, never duplicates.
- **Same-project invariant** - every write runs `PieceMapping.ValidateCrossProject` against the project `DonorLibrary` the service was constructed with; a donor or patch from another project is rejected with no partial state.
- **Kind checks** - a patch `Kind` cannot be assigned as the main donor; `AttachPatch` requires `patchAsset.Kind` to match the requested layer (a full replacer or a `PhysicsPatch` on a body request is rejected).
- **Stale-instance safety** - `AttachPatch`/`DetachPatch` rebuild from the authoritative in-list mapping (by Id) rather than the caller-supplied instance, so attaching a second layer cannot clobber a previously attached one.
- **Guards** - an empty donor mesh path throws `PieceMapping`'s ctor guard; an unknown target piece throws before any state changes.

## Done is an overlay, not a fifth status

`Done` is a caller-side boolean overlay (`doneOverrides`), combined only by `GetOverhaulProgress`:

```
user-facing set state = Done          if  GetArmorSetStatus(...) == Mapped  AND  doneOverrides[setId] == true
                      = GetArmorSetStatus(...)   otherwise
```

The five progress buckets are mutually exclusive and always sum to the total: `Done + Mapped + InProgress + NeedsPatch + NotStarted == TotalSets` (a `Mapped` set lands in the `Mapped` bucket, or in `Done` when `doneOverrides[setId]` is true). The Phase 5 export gate reads the combined `Mapped AND doneOverride`, never a bare `Status == Done`.

> Note: the original plan text wrote `Done + InProgress + NeedsPatch + NotStarted == TotalSets`, omitting the `Mapped` bucket. That expression only holds when every mapped set is also marked done; the accurate always-true invariant includes `Mapped` (Sprint 3.3.4 asserts the five-bucket form).

## Patch detection (Sprint 3.2)

`NeedFor(mapping, donorAsset, patchAssetBody?, patchAssetPhysics?, policy)` returns the missing layer(s) as a `PatchRequirement` (`None / Body / Physics / Both`). Flags are the **combined** flags of the main donor OR the attached per-layer patch asset:

- Body layer satisfied: main donor `DetectedBodySlideFiles` non-empty OR the attached body patch's `DetectedBodySlideFiles` non-empty.
- Physics layer satisfied: main donor `DetectedPhysicsFiles` non-empty OR the attached physics patch's `DetectedPhysicsFiles` non-empty.
- `Loose`: the OR above is the whole rule, so no layer is ever demanded (the donor's own flags ARE the satisfaction) -> `None`.
- `RequireBodyConversion`: additionally demands the body layer when the donor set has a body piece (a `32 ` slot) and neither a BodySlide flag nor an explicit body-type marker in the donor mesh path is present.
- `RequirePhysics`: demands the physics layer when no physics flags are present.

`BodyMarkerFromPath(donorMeshPath)` maps path tokens (case-insensitive) to `BodyType`: `3ba` -> ThreeBA (also `3baf`), `cbbe` -> CBBE, `bhunp` -> BHUNP, `himbo` -> HIMBO, `unp|unpb` -> BHUNP marker, `no token` -> null. It is the explicit, path-only form of roadmap 5.3 "mesh path implies the target body" - gated by the Sprint 3.4 real-donor spot-check and reducible to "BodySlide flag only" (a one-line table edit) if no real donor needs it.

Patch recommendations: `RecommendPatches(donorLibrary, requirement)` returns candidate `DonorAsset`s of the matching `Kind` (`Body`/`Both` -> `BodyConversionPatch`; `Physics`/`Both` -> `PhysicsPatch`), sorted deterministically (`BodyConversionPatch` before `PhysicsPatch`, then by `ImportId`). `None` -> empty.

## Status derivation (Sprint 3.3)

```
MappingStatus: not assigned (null mapping) -> Pending; required layer missing -> NeedsPatch; else -> Mapped   (Sprint 3.2)

ArmorSetStatus (per set, per gender - every target piece of every variant evaluated):
  no piece mapped      -> NotStarted
  any piece NeedsPatch -> NeedsPatch
  every piece mapped   -> Mapped
  else                 -> InProgress

OverhaulProgress: per-set counts; a set is Done exactly when Mapped AND doneOverrides[setId].
  SetDone(catalogSet, mappings, bool) is the ONLY way to set the overlay (records done only when the set is Mapped).
```

## Testing Strategy

- Framework: xUnit + FluentAssertions (same as the rest of the suite); `dotnet test` is the only runner.
- No external data for `Category!=Integration`: the `Catalog` is `SyntheticCatalogUniverse` and donors are `DonorAsset` fixtures - the layer is pure over in-memory `Core` types.
- Determinism: the service is a pure function of its inputs - same catalog + donor set + assign sequence yields the same statuses/progress.
- Sprints 3.1-3.3 add the CRUD/validation, patch-detection, and status/progress suites; Sprint 3.4 adds the real-donor Integration spot-check (auto-skips without `ModsForTests/Armor`).
- Current count (Sprint 3.3): 3 skeleton + 13 CRUD + 12 patch-detection + 7 status/progress = 35 Mapping tests; full suite 492 passing (was 485), 0 warnings / 0 errors on Release build.
