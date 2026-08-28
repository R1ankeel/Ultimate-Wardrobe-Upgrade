# Mapping: Manual Mapping + Patch Detection

> Phase 3, Sprints 3.0-3.4 done - `src/UltimateWardrobe.Mapping` - manual mapping logic that binds a target `Piece` (from a Phase 1 `Catalog`) to a donor piece (from a Phase 2 `DonorAsset`) with optional body-conversion and physics patch layers, then derives `NeedsPatch`, per-set `ArmorSetStatus`, and Overhaul progress. Only `Core` is referenced (the `Catalog` and `DonorAsset` instances are passed in as arguments - no `Scanner`/`DonorLibrary` dependency). Phase 3 complete; Phase 4 (persistence) is next in `Plans/phase3.md`.

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

The mapping graph (a target `Piece` per gender resolved through a `PieceMapping` to a donor piece + up to two patch layers):

```
Target ArmorSet (catalog)
  -> Variant[gender]
    -> Piece (TargetPieceEditorId)
      -> PieceMapping (UniqueKey = OverhaulId : TargetPieceEditorId : TargetGender)
        -> DonorAsset (main donor, FullReplacer only)
        -> BodyConversionPatchAssetId?   (DonorAsset, Kind == BodyConversionPatch)
        -> PhysicsPatchAssetId?          (DonorAsset, Kind == PhysicsPatch)
      -> derives MappingStatus (Mapped / NeedsPatch / Pending) from combined flags + Policy
  -> per-set ArmorSetStatus (NotStarted / InProgress / Mapped / NeedsPatch)
OverhaulProgress = per-set counts over the whole Catalog (+ caller-side Done overlay).
```

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
| `tests/.../Mapping/MappingDeterminismTests.cs` | Determinism suite (2 tests, Sprint 3.4.1) - same assign sequence -> same statuses/progress every run. |
| `tests/.../Mapping/RealDonorPatchDetectionIntegrationTests.cs` | Integration spot-check (1 test, Sprint 3.4.2) - real Red Hood - HIMBO flags drive `NeedFor`/`GetStatus`. |

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

`BodyMarkerFromPath(donorMeshPath)` maps path tokens (case-insensitive) to `BodyType`: `3ba` -> ThreeBA (also `3baf`), `cbbe` -> CBBE, `bhunp` -> BHUNP, `himbo` -> HIMBO, `unp|unpb` -> BHUNP marker, `no token` -> null. It is the explicit, path-only form of roadmap 5.3 "mesh path implies the target body" - confirmed by the Sprint 3.4 real-donor spot-check (Red Hood - HIMBO: `BodyConversionPatch`, 2 BodySlide + 10 physics flags; a piece mapped to it reads `None`/`Mapped` under both `Require*` policies).

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

- Framework: xUnit + FluentAssertions (same as the rest of the suite); `dotnet test` is the only runner. Tests live in `tests/UltimateWardrobe.Tests/Mapping/` as four cohesive unit suites + one Integration suite.
- No external data for `Category!=Integration`: the `Catalog` is `SyntheticCatalogUniverse` and donors are `DonorAsset` fixtures - the layer is pure over in-memory `Core` types. Unit suites: `MappingServiceSkeletonTests` (3), `MappingServiceCrudTests` (13, Sprint 3.1), `PatchDetectionTests` (12, Sprint 3.2), `SetStatusProgressTests` (7, Sprint 3.3), `MappingDeterminismTests` (2, Sprint 3.4.1).
- Determinism (Sprint 3.4.1): the service is a pure function of its inputs - running the same assign sequence over the same synthetic catalog + donor set (regardless of newly generated GUIDs) yields identical per-set statuses and Overhaul progress every run; two tests lock this explicitly. This is the guarantee the Phase 6 UI and Phase 5 export checklist rely on.
- Integration (Sprint 3.4.2): `RealDonorPatchDetectionIntegrationTests` is `[Trait("Category","Integration")]` and auto-skips (with an output note) when `ModsForTests/Armor` has no "Red Hood - HIMBO" archive. It classifies the real donor (`BodyConversionPatch`, 2 BodySlide + 10 physics flags) and asserts a piece mapped to it reads `PatchRequirement.None` under `RequireBodyConversion`/`RequirePhysics` and `MappingStatus.Mapped` under `RequireBoth` - proving `NeedsPatch` reacts to the real classifier flags (it would read `NeedsPatch` if the donor carried none). Temp extraction cleans up (`%TEMP%/UW_Donor_Map_*`) on every path.
- Current count (Sprint 3.4): 3 + 13 + 12 + 7 + 2 + 1 = 38 Mapping tests; full suite 495 passing (481 non-integration + 14 Integration), 0 warnings / 0 errors on Release build, no artifacts left.
