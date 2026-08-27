# Folder Catalog Scanner

> Phase 1, Sprints 1.0-1.7 done - `src/UltimateWardrobe.Scanner` - Mutagen folder-only reading, ARMO -> ARMA -> files correlation, ArmorSet grouping, gender/weight variant assembly, end-to-end `Catalog` scanning plus cache/report, structured logging, committed goldens + Integration-gated real-data tests.

## Overview

`FolderCatalogScanner` produces an `UltimateWardrobe.Core` `Catalog` from a folder on disk only (no MO2, no load order, no BSA unpacking, no running game). The code path is generic for two source kinds: vanilla (a Skyrim game root) and story mod (an extracted mod folder plus a main plugin). Masters-first artificial ordering resolves FormLinks via each plugin's own HEDR `MasterReferences`.

All reading goes through Mutagen binary overlays (`SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE)`) for lazy, fast access. Only the needed record groups are parsed per plugin.

## Components

| File | Purpose |
|------|---------|
| `PluginDiscovery.cs:1` | Data folder resolution, `*.esm`/`*.esl` enumeration, story-mod main + master validation |
| `LoadOrderBuilder.cs:1` | Masters-first recursive order (cycle-safe), dedupe, alphabetical tail |
| `ModLoader.cs:1` | Per-plugin overlay load, degrade on corrupt plugins (skip + warning) |
| `RecordIndex.cs:1` | Merged `Dictionary<FormKey, T>` for ARMO/ARMA/KEYW/TXST + OTFT/RACE extension |
| `ArmorCorrelator.cs:1` | ARMO -> first-resolvable ARMA -> mesh/texture paths (`CorrelatedArmor`) |
| `FileResolver.cs:1` | Logical loose-file existence across Data / no-Data layouts, `MissingFiles` accounting |
| `PlayableRaceFilter.cs:1` | Playable-race whitelist (10 base + 10 vampire RACE EditorIDs, verified against real Skyrim.esm) + `DefaultRace` universal fallback |
| `KeyNormalizer.cs:1` | EditorID / Outfit EditorID / mesh-folder -> normalized set key + DisplayName |
| `PieceTypeDetector.cs:1` | Piece-type word from EDID suffix + BOD2 slot cross-check |
| `OutfitSetKeyResolver.cs:1` | OTFT membership -> priority set key (deterministic tie-break) |
| `ArmorSetGrouper.cs:1` | Creature pre-filter -> Outfit-first -> EDID/mesh fallback -> `GroupedSet`s + skip counts |
| `BipedSlotMapper.cs:1` | Frozen BOD2 slot table (from planning 1.0.5), `SlotIndex` + `ToSlotString` |
| `GenderWeightDetector.cs:1` | WeightClass from KEYW (ArmorType bonus), gender from ID/mesh/ARMA signals |
| `VariantAssembler.cs:1` | `(Gender, Weight)` variants per `ArmorSet`, piece split + ordering |
| `FolderCatalogScanner.cs:1` | Sprint 1.5 orchestrator: discovery -> order -> index -> correlate -> group -> assemble -> `Catalog`; structured `ILogger<T>` milestone events (Sprint 1.7) |
| `ScanReportBuilder.cs:1` | Warning dedup/sort, `ScanStats` fill, per-record exception routing to `CatalogScanException` |
| `CatalogCacheStore.cs:1` | Canonical JSON persistence, `CatalogSource` converter, `IsFresh` probe comparison |

## Catalog scan (Sprint 1.5)

`FolderCatalogScanner.ScanAsync(CatalogSource, CancellationToken)` runs 1.1-1.4 as one pipeline: discovery -> masters-first order -> overlay load -> merged `RecordIndex` -> `ArmorCorrelator` (with `FileResolver` wiring) -> `ArmorSetGrouper` -> `VariantAssembler`, then missing-file accounting and deterministic `Catalog` assembly. Cancellation is checked between plugins and between record groups; the method is `Task`-friendly - it never throws synchronously, surfacing all outcomes through the returned `Task` (success, `Task.FromCanceled<Catalog>` on cancellation, `Task.FromException<Catalog>` otherwise). Loaded overlays are disposed in a `finally`.

### ScanReport (Sprint 1.7)

`ScanReport` lives in `UltimateWardrobe.Core` and rides on `Catalog.Report`; `BuildSummary()` renders the compact one-line diagnostics for the Phase 6 UI. `ScanReportBuilder` (Scanner) dedups warnings by `(Message, EditorId)`, sorts them by Message then EditorId (ordinal), and fills `ScanStats`: `Skipped` is the sum of the `SkippedByReason` breakdown stored in a `SortedDictionary<SkipReason, int>` (thus sorted by enum value). `OutfitGroupedSetCount` counts sets whose members were placed via an Outfit (`ArmorSetGrouper.GroupingResult.OutfitGroupedSetCount`, per-group `GroupedSet.GroupedViaOutfit`). Each pipeline stage wraps its work in `ScanReportBuilder.Guard<T>(stage, editorId, action)`, which passes `CatalogScanException` and `OperationCanceledException` through unchanged and wraps anything else in a `CatalogScanException` whose `EditorId` carries the offending record. The scanner exposes the latest report as `LastReport`.

### Structured logging (Sprint 1.7)

`FolderCatalogScanner` takes an optional `ILogger<FolderCatalogScanner>` (default `NullLogger<FolderCatalogScanner>.Instance`); every event carries the scan id: `scan started` (source kind/root), debug `source details` and `plugin loaded (i/N)`, `record index built (ARMO/ARMA)`, `grouped N armors into M sets (K outfit-grouped); skipped S`, `finished in N ms with W warnings`, plus `missing master` and `plugin failed to load` warnings. `ListLogger<T>` (test project) collects these so logging tests assert the messages - no logging framework is required.

### CatalogCacheStore

`CatalogCacheStore` persists a `Catalog` to canonical System.Text.Json (camelCase, string enums, built-in type ctor binding - Core types stay attribute-free). `CatalogSource` uses a custom converter with a `kind` discriminator: `"vanilla"` (`rootPath` + `pluginNames`) or `"story"` (`rootPath` + `mainPlugin` + `masters`). The file wrapper is `CacheFile { FormatVersion = 1, Probe, Catalog }`. `BuildProbe` captures the source root (full-path normalized) plus one `PluginProbe` (Name, Length, LastWriteTimeUtc) per plugin exactly as discovery would enumerate it; `IsFresh(path, source)` retries a fresh probe and returns true only when the file parses and both probes match. `Save` writes atomically under `FileShare.Read` so `TryLoad`/`IsFresh` can run on the same file.

## RecordIndex (Sprint 1.1 + 1.3.0)

- Later file wins (override semantics) across the artificial order.
- KEYW cache is sparse: only `ArmorHeavy` / `ArmorLight` / `ArmorClothing` EditorIDs.
- TXST cache is sparse: only texture sets referenced by an ARMA `SkinTexture`.
- RACE cache (1.3.0) is sparse and lazy: built from the FormKeys referenced by `ARMA.Race`, EditorID read only.
- OTFT cache (1.3.0) holds every Outfit plus the reverse `armor FormKey -> HashSet<FormKey>` membership map `OutfitsForArmor`.

## Grouping heuristic (Sprint 1.3)

### Creature-skin pre-filter

Before any grouping the grouper resolves the primary (first resolvable) ARMA race link. A RACE whose EditorID is outside the playable whitelist (real Skyrim IDs: `ArgonianRace`/`BretonRace`/`DarkElfRace`/`HighElfRace`/`ImperialRace`/`KhajiitRace`/`NordRace`/`OrcRace`/`RedguardRace`/`WoodElfRace` plus every `*Vampire` variant from `ArgonianRaceVampire` to `WoodElfRaceVampire`, verified on the real scan) skips the ARMO with `SkipReason.CreatureRace`, counted in `Stats.Skipped` and broken out per reason in `Stats.SkippedByReason`. Vanilla universal armor links its ARMA to a plain `DefaultRace` (FormID 0x000019) instead of a null link - `DefaultRace` is whitelisted too, so 3200+ records are never skimmed by mistake. A null race link never skips. An unresolvable race link emits a `ScanWarning` and the record is kept (EDID/mesh fallback).

### Outfit-first stage (Sprint 1.3, agreement rule 1.7.3)

An ARMO belonging to at least one OTFT collects candidate keys - the EDID/mesh fallback key (stage 2) plus every normalized Outfit EditorID (same `KeyNormalizer` pipeline, no piece-suffix strip). `ArmorSetGrouper.MergeByAgreement` scores each candidate by how many same-family members vote for it and keeps the top-voting key, tie-broken alphabetically, so multi-outfit armor lands with the set the majority of its pieces actually belong to. Before the vote `FilterWardrobeOutfits` drops NPC-wardrobe outfit keys whose carriers span more than one EDID family unless every carrier in that outfit is exclusive to it (`OutfitIds.Count == 1`); a named armor Outfit like `IronArmor` survives, while generic compositions such as `cwmission04outfitimperial` (which dress unrelated families in base-game armor) no longer swallow whole sets. Armor in no Outfit falls through to stage 2.

### EDID/mesh fallback stage

`KeyNormalizer` strips CC prefixes (`cc*-ba_`), set prefixes (`Armor`, `Clothes`, `Clothing`, `AA`, `AANord`, `DLC1`, `DLC2`, `zzz`), the `AA`/`ba` marker, piece suffixes (`Cuirass`, `Gauntlets`, `Boots`, `Helmet`, `Hood`, `Shield`, `Circlet`, `Plate`, `Robe`, ...), a single trailing variant letter when the stem still ends in a piece suffix (`ArmorSteelBootsA` -> `steel`, so the A/B-divided Steel set groups under one key; 1.7.3), and stop words (`No`, `Yes`), then keeps alphanumerics, lowercases invariant, and produces a CamelCase Title-case `DisplayName`. If no meaningful middle remains, the ARMA mesh folder segment after `armor`/`clothes` (suffixes `male`/`female`/`_0`/`_1`/`_1st` stripped) is used.

### Split membership

A set whose cuirass+helmet live in one OTFT while shared gauntlets+boots belong to no Outfit still joins into ONE `ArmorSet` because the fallback half's EDID normalizes to the same key as the Outfit EditorID (e.g. `DLC2NordicCarved` and `DLC2NordicCarvedGauntlets` -> both `nordiccarved`). This is asserted explicitly by the mini-universe test.

### Garbage filtering

No-ARMA -> `NoArmature`, every ARMA empty model -> `EmptyModel`, no BOD2 slot -> `NoSlot`, Body-slot without an armor weight keyword -> `NoKeyword`, unresolvable key -> `Other`. Each skip is counted and optionally warned.

## Ordering

Sets ordered by normalized Id (ordinal). Members within a set ordered by BOD2 slot order (Head, Hair, Body, Hands, Forearms, Amulet, Ring, Feet, Calves, Shield, Tail, LongHair, Circlet, Ears) then by EditorId - via `BipedSlotMapper.SlotIndex`. Deterministic across scans.

## Gender / Weight split (Sprint 1.4)

### WeightClass (WeightClothing, ArmorType bonus)

`GenderWeightDetector.DetectWeight` resolves WeightClass from keywords first, priority Heavy > Light > Clothing. Without a weight keyword it falls back to `BodyTemplate.ArmorType` (`HeavyArmor` -> Heavy, `LightArmor` -> Light, `Clothing` -> Clothing), and without either it returns `Any`. An unresolvable keyword link leaves the record at this fallback, never `Any` prematurely.

### Gender (signals)

Explicit markers win before any ARMA signal reading:

1. EditorID suffix tokens, longest first, case-insensitive: `_female`, `-female`, `_male`, `-male`, `female`, `male`, `_f`, `-f`, `_m`, `-m`.
2. ARMA mesh path folder segments (`female` or `male`); both genders present in the same path is ambiguous and falls through to signals.

Then ARMA signals per gender side: a non-null `WorldModel.{Gender}.File` OR the `WeightSliderEnabled.{Gender}` bool. A gender side counts as present when either signal is true. Then `RaceGenderHint` (RACE EditorID contains `female`/`male`). If nothing resolves, gender is `Unisex` with a `ScanWarning` (`Unisex` variants are skipped by the catalog scanner by design; on a real vanilla scan the signal set effectively never leaves an unresolved side).

### Variant assembly

`VariantAssembler.Assemble` turns one `ArmorSet` into variants - one per (Gender, Weight) combination. The same ARMO backed by two gender-specific ARMA yields two Pieces (same EditorId, different gender) - matching `PieceMapping.UniqueKey` (`OverhaulId + TargetPieceEditorId + TargetGender`). Pieces are slot-ordered via `BipedSlotMapper`, tie-broken by EditorId. An unrecognized BOD2 flag set falls back to a `BODT {uint}` slot string instead of failing.

Frozen `Piece.Slot` format (planning 1.0.5) is produced by `BipedSlotMapper.ToSlotString`: `"{BODTnumber} {Name}"`, e.g. `32 Body`, `33 Hands`, `37 Feet`.

### Mutagen notes (probes)

`AssetLink<SkyrimModelAssetType>.TrySetPath` returns false for bare filenames without a folder (e.g. `male.nif`) - test fixtures must use full paths like `meshes/armor/iron/cuirass_1.nif`. In-memory writers round-trip `WeightSliderEnabled` bools immediately, but a `Model.File` assigned to a writer object only becomes visible through a parse/read.

## Tests

- `tests/UltimateWardrobe.Tests/Scanner/` - unit suites for discovery, order, indexing, correlation, grouping, and variant assembly.
- `SyntheticGroupingUniverse.cs` - runtime mini-plugin via the Mutagen writer: an Outfit-driven set (Iron), a split-membership set (Nordic Carved), a fallback set (Leather), a creature-skin record (Boar), a vampire-race record, a multi-outfit armor, an unresolvable-race record, and one record per garbage skip reason.
- Sprint 1.3 suite (92 tests) includes the explicit never-fragment assertion for the split-membership set and per-reason skip-count tracking.
- Sprint 1.4 suite (44 new tests) covers the slot table + ordering, the weight matrix (keyword priority, ArmorType bonus, Any), gender signals (explicit ID/mesh, model + slider signals, race hint, ambiguous mesh, Unisex fallback with warning), the Iron acceptance (Male Heavy + Female Heavy variants, `0A2C8841/0A2C8842/0A2C8843` on `32 Body`/`33 Hands`/`37 Feet`, same EditorId across genders), same-ARMO two-piece output, determinism, and the `BODT {uint}` raw-slot fallback.
- Sprint 1.5 suite (27 new tests) covers end-to-end determinism (scan twice, canonical JSON equal) and expected stats on the synthetic universe (17 ARMO / 16 ARMA, 6 sets, 3 Outfit-grouped, 5 skipped by reason, 24 missing files), the two warning producers (MysteryGauntlets unresolvable race, DanglingOnly dangling armature), missing-root / missing-main-plugin error routing, pre-cancelled token, warning dedup/sort, `ScanStats` fill, `Guard` exception routing with `EditorId`, cache round-trip value identity (vanilla + story sources), canonical byte-equal saves, and each `IsFresh` stale condition (modified plugin, deleted plugin, missing cache file, corrupt cache).
- Sprint 1.6 suite (14 golden + negative-path tests, committed) - `SyntheticGroupingUniverse` writer + mini-universe happy path; one static golden plugin committed under `tests/TestData/Plugins/MiniUniverse.esp` guards the reader across refactors; golden catalog JSON under `tests/TestData/CatalogGolden/` regenerated only under `UW_WRITE_GOLDENS=1`. Golden comparison normalizes the scan root (`source.rootPath` -> `<root>` placeholder) because every scan uses a fresh temp dir. Mini-universe stats: TotalArmo 23, TotalArma 22, GroupedSets 12, MissingFiles 40, skipped 2 (NoArmature 1, CreatureRace 1). Negative paths: corrupt/empty plugin, missing main plugin, missing master.
- Real-game tests are `[Trait("Category","Integration")]` and skip automatically when the game folder is absent: the vanilla scan asserts Iron armor present, `TotalArmo > 500`, `GroupedSets > 50`, at least one creature-race skip, and a <= 10 s wall time (~1.2 s on this machine); `Vanilla_RealGame_FullKitsAreSingleSets_NoMegaSets` (1.7.3) asserts the plain Iron/Steel/Leather full kits land in ONE set each (Steel with Male + Female variants), every set has <= 150 pieces, and no set mixes the Iron and Steel cuirasses; the story-mod scan extracts a VIGILANT rar from `ModsForTests/QuestExpansiaon` to `%TEMP%/UW_Scan_*` with the Phase 0 extractor, asserts > 0 sets, and cleans up.

## Status

The pipeline is complete end-to-end (`ScanAsync(CatalogSource, ct)` -> deterministic `Catalog`, persisted via `CatalogCacheStore` with probe invalidation, diagnostics via `Catalog.Report` + structured logging). Sprint 1.6 shipped repeatable CI output (committed golden plugin + catalog JSON, no external data needed for `Category!=Integration`) and real-data coverage gated behind `Category=Integration`. Sprint 1.7 shipped the report on `Catalog.Report`, structured `ILogger<T>` events, and the heuristic tuning pass: the variant-letter strip unifies the A/B Steel variants under `steel`, and the wardrobe filter removes NPC-composition outfits. On real vanilla the scan groups 4197 ARMO into 3396 sets in ~1.5 s with 377 skips: the plain Iron kit (helmet/cuirass/gauntlets/boots/shield) is ONE `iron` set, Steel groups its 17 pieces (Male + Female Heavy variants) into `steel`, Leather is one 4-piece `leatheralloutfit` set, `bandedironalloutfit`/`cwmission04outfitimperial`-style compositions no longer produce mega-sets (largest set `blades` = 94 pieces), and the set-integrity test guards the kit boundaries.