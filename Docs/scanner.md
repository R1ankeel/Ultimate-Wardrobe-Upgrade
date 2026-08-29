# Folder Catalog Scanner

> Phase 1, Sprints 1.0-1.7 done (+ Sprint 6.8 filter additions, + Sprint 6.9 official-masters-only vanilla discovery, resolution-only Update.esm, and the shared-mesh enchantment-variant filter, + Sprint 6.10 DLC-prefixed enchanted-variant word-boundary token rule) - `src/UltimateWardrobe.Scanner` - Mutagen folder-only reading, ARMO -> ARMA -> files correlation, ArmorSet grouping, gender/weight variant assembly, end-to-end `Catalog` scanning plus cache/report, structured logging, committed goldens + Integration-gated real-data tests.

## Overview

`FolderCatalogScanner` produces an `UltimateWardrobe.Core` `Catalog` from a folder on disk only (no MO2, no load order, no BSA unpacking, no running game). The code path is generic for two source kinds: vanilla (a Skyrim game root) and story mod (an extracted mod folder plus a main plugin). Masters-first artificial ordering resolves FormLinks via each plugin's own HEDR `MasterReferences`.

All reading goes through Mutagen binary overlays (`SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE)`) for lazy, fast access. Only the needed record groups are parsed per plugin.

## Components

| File | Purpose |
|------|---------|
| `PluginDiscovery.cs:1` | Data folder resolution, `*.esm`/`*.esl` enumeration; an empty `pluginNames` list (the App's default `VanillaCatalogSource`) scans ONLY the four official master files - `Skyrim.esm`/`Dawnguard.esm`/`HearthFires.esm`/`Dragonborn.esm` - plus `Update.esm` as a RESOLUTION-ONLY baseline (linked for master resolution, never scanned for armor; Sprint 6.9), warning for each missing official master |
| `LoadOrderBuilder.cs:1` | Masters-first recursive order (cycle-safe), dedupe, alphabetical tail |
| `ModLoader.cs:1` | Per-plugin overlay load, degrade on corrupt plugins (skip + warning) |
| `RecordIndex.cs:1` | Merged `Dictionary<FormKey, T>` for ARMO/ARMA/KEYW/TXST + OTFT/RACE extension |
| `ArmorCorrelator.cs:1` | ARMO -> all-resolvable ARMAs -> per-gender mesh/texture paths (`CorrelatedArmor` with `MeshPathMale`/`MeshPathFemale`/`AllAddons`, F2) |
| `FileResolver.cs:1` | Logical loose-file existence across Data / no-Data layouts, `MissingFiles` accounting |
| `PlayableRaceFilter.cs:1` | Playable-race whitelist (10 base + 10 vampire RACE EditorIDs, verified against real Skyrim.esm) + `DefaultRace` universal fallback |
| `KeyNormalizer.cs:1` | EditorID / Outfit EditorID / mesh-folder -> normalized set key + DisplayName |
| `PieceTypeDetector.cs:1` | Piece-type word from EDID suffix + BOD2 slot cross-check |
| `OutfitSetKeyResolver.cs:1` | OTFT membership -> priority set key (deterministic tie-break) |
| `ArmorSetGrouper.cs:1` | Creature pre-filter -> Outfit-first -> EDID/mesh fallback -> `GroupedSet`s + skip counts |
| `VanillaEnchantmentFilter.cs:1` | Vanilla enchantment name-suffix skip (longest-first word match, `OrdinalIgnoreCase`) |
| `BipedSlotMapper.cs:1` | Frozen BOD2 slot table (from planning 1.0.5), `SlotIndex` + `ToSlotString` |
| `GenderWeightDetector.cs:1` | WeightClass from KEYW (ArmorType bonus), gender from ID/ARMA signals/mesh fallback (F1: ARMA signals win over mesh) |
| `VariantAssembler.cs:1` | `(Gender, Weight)` variants per `ArmorSet`, per-gender mesh split + ordering (F2) |
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

#### Id discoverability - F3 clarification (no code change)

Outfit-driven Ids are winner keys when at least one member belongs to an outfit. `steel` vs `steelplate` is by material (Steel = `ArmorSteel*` -> `steel`, Steel Plate = `ArmorSteelPlate*`/`NordPlate` -> `steelplate` or `nordicplate`/`steelplatealloutfit` when outfit `SteelPlateAllOutfit` wins) - display-name search is the supported UI, not Id exact match. Known vanilla aliases for consumer code (not scanner): `Fur Armor` -> `bandit` (`ArmorBandit*` EditorIDs), `Leather Armor` -> `leatheralloutfit` (outfit `LeatherAllOutfit` wins over EDID `leather` because all 4 carriers share one family, so kept), `Chitin Armor` light -> `chitin` vs `Chitin Heavy Armor` -> `chitinheavy`, `Scaled Horn Armor` -> `scaled` (same family as `Scaled Armor` plus `ScaledHorn` variant). `FilterWardrobeOutfits` verification: `leatheralloutfit` has `families.Count == 1` so kept - correct; `cwmission04outfitimperial` has `families.Count > 1` (Iron/Steel/Leather) and not allExclusive (`OutfitIds.Count != 1` for some carriers) -> dropped - correct. No change unless new fragmentation found.

### EDID/mesh fallback stage

`KeyNormalizer` strips CC prefixes (`cc*-ba_`), set prefixes (`Armor`, `Clothes`, `Clothing`, `AA`, `AANord`, `DLC1`, `DLC2`, `zzz`), the `AA`/`ba` marker, piece suffixes (`Cuirass`, `Gauntlets`, `Boots`, `Helmet`, `Hood`, `Shield`, `Circlet`, `Plate`, `Robe`, ...), a single trailing variant letter when the stem still ends in a piece suffix (`ArmorSteelBootsA` -> `steel`, so the A/B-divided Steel set groups under one key; 1.7.3), and stop words (`No`, `Yes`), then keeps alphanumerics, lowercases invariant, and produces a CamelCase Title-case `DisplayName`. If no meaningful middle remains, the ARMA mesh folder segment after `armor`/`clothes` (suffixes `male`/`female`/`_0`/`_1`/`_1st` stripped) is used.

### Split membership

A set whose cuirass+helmet live in one OTFT while shared gauntlets+boots belong to no Outfit still joins into ONE `ArmorSet` because the fallback half's EDID normalizes to the same key as the Outfit EditorID (e.g. `DLC2NordicCarved` and `DLC2NordicCarvedGauntlets` -> both `nordiccarved`). This is asserted explicitly by the mini-universe test.

### Garbage filtering

No-ARMA -> `NoArmature`, every ARMA empty model -> `EmptyModel`, no BOD2 slot -> `NoSlot`, Body-slot without an armor weight keyword -> `NoKeyword`, unresolvable key -> `Other`. Each skip is counted and optionally warned.

**Jewelry + vanilla-enchantment skip (Sprint 6.8).** A `BipedFlags` Amulet or Ring skip is `SkipReason.Jewelry` (rings and necklaces are not armor-row material for the Phase 6 matrix); an `Armor.Name` ending in a vanilla enchantment suffix (matched longest-first, `OrdinalIgnoreCase`, e.g. `of Muffle`, `of Alteration & Magicka Regen`, lowercase variants - the exact word list lives in `VanillaEnchantmentFilter`) skips as `SkipReason.Enchanted`. Both checks run inside `ClassifyGarbage` AFTER `NoArmature`/`EmptyModel`/`NoSlot` and BEFORE `NoKeyword`, so the existing counts and the creature-skin pre-filter order are untouched.

## Ordering

Sets ordered by normalized Id (ordinal). Members within a set ordered by BOD2 slot order (Head, Hair, Body, Hands, Forearms, Amulet, Ring, Feet, Calves, Shield, Tail, LongHair, Circlet, Ears) then by EditorId - via `BipedSlotMapper.SlotIndex`. Deterministic across scans.

## Gender / Weight split (Sprint 1.4)

### WeightClass (WeightClothing, ArmorType bonus)

`GenderWeightDetector.DetectWeight` resolves WeightClass from keywords first, priority Heavy > Light > Clothing. Without a weight keyword it falls back to `BodyTemplate.ArmorType` (`HeavyArmor` -> Heavy, `LightArmor` -> Light, `Clothing` -> Clothing), and without either it returns `Any`. An unresolvable keyword link leaves the record at this fallback, never `Any` prematurely.

### Gender (signals) - F1 fix

ARMA signals now win over mesh folder (mesh is fallback only when signals absent) - prevents `Armor/Iron/Male/...` with both male and female world models being forced to Male-only:

1. EditorID suffix tokens, longest first, case-insensitive: `_female`, `-female`, `_male`, `-male`, `female`, `male`, `_f`, `-f`, `_m`, `-m` - explicit gender-specific ARMO, wins over every ARMA signal.
2. ARMA signals per gender side: a non-null `WorldModel.{Gender}.File` OR the `WeightSliderEnabled.{Gender}` bool. A gender side counts as present when either signal is true - both signaled -> Male + Female; one -> that gender.
3. Mesh path folder segments (`female` or `male`) - fallback only when ARMA signals are absent for both genders; both genders present in the same path is ambiguous and falls through to race hint.
4. `RaceGenderHint` (RACE EditorID contains `female`/`male`).

If nothing resolves, gender is `Unisex` with a `ScanWarning` (`Unisex` variants are skipped by the catalog scanner by design; on a real vanilla scan the signal set effectively never leaves an unresolved side). **Atomicity note:** F1 (this precedence fix) without F2 (per-gender mesh storage in `ArmorCorrelator`/`VariantAssembler`) makes female Iron appear in the catalog with the wrong (male) mesh - invisible in-game bug worse than the current visible absence. F1 and F2 must ship atomically (see `Plans/scaner-filtration.md:120`).

### Variant assembly - F2 per-gender mesh

`VariantAssembler.Assemble` turns one `ArmorSet` into variants - one per (Gender, Weight) combination. The same ARMO backed by two gender-specific ARMA yields two Pieces (same EditorId, different gender) - matching `PieceMapping.UniqueKey` (`OverhaulId + TargetPieceEditorId + TargetGender`). F2 fix: per-gender mesh is allocated (`MeshPathMale` for Male variant, `MeshPathFemale` for Female, aggregated `AllAddons` for texture sets), so Iron Male uses `Armor/Iron/Male/...` and Iron Female uses `Armor/Iron/F/...` instead of sharing the male mesh. Pieces are slot-ordered via `BipedSlotMapper`, tie-broken by EditorId. An unrecognized BOD2 flag set falls back to a `BODT {uint}` slot string instead of failing.

Frozen `Piece.Slot` format (planning 1.0.5) is produced by `BipedSlotMapper.ToSlotString`: `"{BODTnumber} {Name}"`, e.g. `32 Body`, `33 Hands`, `37 Feet`.

### Mutagen notes (probes)

`AssetLink<SkyrimModelAssetType>.TrySetPath` returns false for bare filenames without a folder (e.g. `male.nif`) - test fixtures must use full paths like `meshes/armor/iron/cuirass_1.nif`. In-memory writers round-trip `WeightSliderEnabled` bools immediately, but a `Model.File` assigned to a writer object only becomes visible through a parse/read.

## Tests

- `tests/UltimateWardrobe.Tests/Scanner/` - unit suites for discovery, order, indexing, correlation, grouping, and variant assembly.
- `SyntheticGroupingUniverse.cs` - runtime mini-plugin via the Mutagen writer: an Outfit-driven set (Iron), a split-membership set (Nordic Carved), a fallback set (Leather), a creature-skin record (Boar), a vampire-race record, a multi-outfit armor, an unresolvable-race record, and one record per garbage skip reason.
- Sprint 6.8/6.9 filter suite (`SyntheticFilteringUniverse.cs` + `ArmorSetGrouperTests`) - a cheap case-insensitive plugin (ring, amulet, circlet, enchanted-name records incl. `&`-combined phrases and lowercase suffixes, plain cuirass, plus the Sprint 6.9 shared-mesh trio - a plain base boot, an `Ench*` variant reusing its exact mesh, and an enchanted robe with a unique mesh, and the Sprint 6.10 DLC quartet - `DLC2EnchArmor...` + `DLC2EnchClothes...` variants sharing a vendor-base mesh, plus `WenchClothes01/02` guarding the word-boundary rule) proves rings/necklaces skip as `Jewelry`, a circlet stays in the catalog, vanilla-enchanted names skip as `Enchanted`, the shared-mesh `Ench*` variant skips as `Enchanted` while its base kit stays, DLC-prefixed enchanted variants skip too, a word like `Wench` embedding "Ench" does NOT trigger the token rule, and the unique-mesh enchanted robe is kept, plain armor survives, and per-reason skip counts track exactly.
- Sprint 1.3 suite (92 tests) includes the explicit never-fragment assertion for the split-membership set and per-reason skip-count tracking.
- Sprint 1.4 suite (44 new tests) covers the slot table + ordering, the weight matrix (keyword priority, ArmorType bonus, Any), gender signals (explicit ID/mesh, model + slider signals, race hint, ambiguous mesh, Unisex fallback with warning), the Iron acceptance (Male Heavy + Female Heavy variants, `0A2C8841/0A2C8842/0A2C8843` on `32 Body`/`33 Hands`/`37 Feet`, same EditorId across genders), same-ARMO two-piece output, determinism, and the `BODT {uint}` raw-slot fallback.
- Sprint 1.5 suite (27 new tests) covers end-to-end determinism (scan twice, canonical JSON equal) and expected stats on the synthetic universe (17 ARMO / 16 ARMA, 6 sets, 3 Outfit-grouped, 5 skipped by reason, 24 missing files), the two warning producers (MysteryGauntlets unresolvable race, DanglingOnly dangling armature), missing-root / missing-main-plugin error routing, pre-cancelled token, warning dedup/sort, `ScanStats` fill, `Guard` exception routing with `EditorId`, cache round-trip value identity (vanilla + story sources), canonical byte-equal saves, and each `IsFresh` stale condition (modified plugin, deleted plugin, missing cache file, corrupt cache).
- Sprint 1.6 suite (14 golden + negative-path tests, committed) - `SyntheticGroupingUniverse` writer + mini-universe happy path; one static golden plugin committed under `tests/TestData/Plugins/MiniUniverse.esp` guards the reader across refactors; golden catalog JSON under `tests/TestData/CatalogGolden/` regenerated only under `UW_WRITE_GOLDENS=1`. Golden comparison normalizes the scan root (`source.rootPath` -> `<root>` placeholder) because every scan uses a fresh temp dir. Mini-universe stats (post-6.8 filter pass): TotalArmo 23, TotalArma 22, GroupedSets 11, MissingFiles 38, skipped 3 (NoArmature 1, CreatureRace 1, Jewelry 1 - the ElvenAmulet). Negative paths: corrupt/empty plugin, missing main plugin, missing master.
- Real-game tests are `[Trait("Category","Integration")]` and skip automatically when the game folder is absent: the vanilla scan asserts Iron armor present, `TotalArmo > 500`, `GroupedSets > 50`, at least one creature-race skip, and a <= 10 s wall time (~0.4 s on this machine); `Vanilla_RealGame_FullKitsAreSingleSets_NoMegaSets` (1.7.3) asserts the plain Iron/Steel/Leather full kits land in ONE set each (Steel with Male + Female variants), every set has <= 150 pieces, and no set mixes the Iron and Steel cuirasses; the story-mod scan extracts a VIGILANT rar from `ModsForTests/QuestExpansiaon` to `%TEMP%/UW_Scan_*` with the Phase 0 extractor, asserts > 0 sets, and cleans up. The Sprint 6.9 pass restricted the default vanilla discovery to the four official master files only (CC `.esm`/`.esl` plato and `_ResourcePack.esl` are no longer enumerated, `VanillaOfficialMasters` whitelist) and linked `Update.esm` as a resolution-only baseline (`VanillaResolutionOnlyBaseline`, no missing-master warning, never scanned for armor) - the real scan now reports TotalArmo 3670, GroupedSets 651, Skips 2768 (Enchanted 2135 + Jewelry 351 + CreatureRace 276 + NoKeyword 6), MissingFiles 1858, 0 warnings.

## Status

The pipeline is complete end-to-end (`ScanAsync(CatalogSource, ct)` -> deterministic `Catalog`, persisted via `CatalogCacheStore` with probe invalidation, diagnostics via `Catalog.Report` + structured logging). Sprint 1.6 shipped repeatable CI output (committed golden plugin + catalog JSON, no external data needed for `Category!=Integration`) and real-data coverage gated behind `Category=Integration`. Sprint 1.7 shipped the report on `Catalog.Report`, structured `ILogger<T>` events, and the heuristic tuning pass: the variant-letter strip unifies the A/B Steel variants under `steel`, and the wardrobe filter removes NPC-composition outfits. The Sprint 6.8 pass added the jewelry + vanilla-enchantment name filters: rings/necklaces and pre-enchanted gear no longer flood the catalog/Phase 6 matrix (the committed mini-universe goldens were regenerated - `GroupedSets 11`, `MissingFiles 38`, `Jewelry 1` skip). The Sprint 6.9 pass restricted vanilla discovery to the four official master files only (`VanillaOfficialMasters` whitelist in `PluginDiscovery`) so CC plato content never leaks into the vanilla catalog, plus the resolution-only `Update.esm` baseline - linked into the load order so the three DLC masters resolve without a missing-master warning, but `RecordIndex.Build` never scans its ARMO/ARMA. `DiscoveredPlugin.IsResolutionOnly` flows through `ModLoader` to `RecordIndex`. Sprint 6.9 also closed the gap in the 6.8 enchantment filter: the name-suffix list still let 737 enchanted records through (98 distinct suffix phrases like `of Major Sneaking`, `of Brawn`, `of Magic Suppression`, `the Knight`), so `ArmorSetGrouper.ClassifyGarbage` gained the shared-mesh rule - an EditorID armor whose "Ench" token sits at a word boundary (start of ID or preceded by a non-letter, so `DLC2Ench...`, `DLC1Ench...`, `EnchClothes...` match while a bare substring inside another word like `WenchClothes01` does not) and whose world-model path is shared by any other ARMO in the scan (`BuildSharedMeshSet`, case-insensitive path grouping) is a base-kit duplicate and skips as `SkipReason.Enchanted`, while enchanted records owning a unique mesh (Master/Expert/Adept/Novice robes, Robes of Quickening, Arch-mage gear) stay. This matches the user rationale: an enchanted variant reuses the unenchanted item's mesh, so the variant row is redundant. On real vanilla the scan now groups 3670 ARMO into 651 base-kit sets in ~0.4 s with 2768 skips (Enchanted 2135 + Jewelry 351 + CreatureRace 276 + NoKeyword 6), 1858 missing files and 0 warnings: the plain Iron kit (helmet/cuirass/gauntlets/boots/shield) is ONE `iron` set, Steel groups its 17 pieces (Male + Female Heavy variants) into `steel`, Leather is one 4-piece `leatheralloutfit` set, `bandedironalloutfit`/`cwmission04outfitimperial`-style compositions no longer produce mega-sets (largest set `blades` = 94 pieces), and the set-integrity test guards the kit boundaries.