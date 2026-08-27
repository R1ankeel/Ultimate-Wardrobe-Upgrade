# Folder Catalog Scanner

> Phase 1, Sprints 1.0-1.4 done - `src/UltimateWardrobe.Scanner` - Mutagen folder-only reading, ARMO -> ARMA -> files correlation, ArmorSet grouping, gender/weight variant assembly.

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
| `PlayableRaceFilter.cs:1` | Playable-race whitelist (10 base + 10 vampire RACE EditorIDs) |
| `KeyNormalizer.cs:1` | EditorID / Outfit EditorID / mesh-folder -> normalized set key + DisplayName |
| `PieceTypeDetector.cs:1` | Piece-type word from EDID suffix + BOD2 slot cross-check |
| `OutfitSetKeyResolver.cs:1` | OTFT membership -> priority set key (deterministic tie-break) |
| `ArmorSetGrouper.cs:1` | Creature pre-filter -> Outfit-first -> EDID/mesh fallback -> `GroupedSet`s + skip counts |
| `BipedSlotMapper.cs:1` | Frozen BOD2 slot table (from planning 1.0.5), `SlotIndex` + `ToSlotString` |
| `GenderWeightDetector.cs:1` | WeightClass from KEYW (ArmorType bonus), gender from ID/mesh/ARMA signals |
| `VariantAssembler.cs:1` | `(Gender, Weight)` variants per `ArmorSet`, piece split + ordering |

## RecordIndex (Sprint 1.1 + 1.3.0)

- Later file wins (override semantics) across the artificial order.
- KEYW cache is sparse: only `ArmorHeavy` / `ArmorLight` / `ArmorClothing` EditorIDs.
- TXST cache is sparse: only texture sets referenced by an ARMA `SkinTexture`.
- RACE cache (1.3.0) is sparse and lazy: built from the FormKeys referenced by `ARMA.Race`, EditorID read only.
- OTFT cache (1.3.0) holds every Outfit plus the reverse `armor FormKey -> HashSet<FormKey>` membership map `OutfitsForArmor`.

## Grouping heuristic (Sprint 1.3)

### Creature-skin pre-filter

Before any grouping the grouper resolves the primary (first resolvable) ARMA race link. A RACE whose EditorID is outside the playable whitelist (Argonian/Breton/DarkElf/HighElf/Imperial/Khajiit/Nord/Orc/Redguard/WoodElf plus every `*Vampire` variant) skips the ARMO with `SkipReason.CreatureRace`, counted in `Stats.Skipped` and broken out per reason in `Stats.SkippedByReason`. A null race link never skips. An unresolvable race link emits a `ScanWarning` and the record is kept (EDID/mesh fallback).

### Outfit-first stage

An ARMO belonging to at least one OTFT gets its key from the normalized Outfit EditorID (same `KeyNormalizer` pipeline, no piece-suffix strip). Multi-outfit armor picks the alphabetically-first normalized key (deterministic). Armor in no Outfit falls through to stage 2.

### EDID/mesh fallback stage

`KeyNormalizer` strips CC prefixes (`cc*-ba_`), set prefixes (`Armor`, `Clothes`, `Clothing`, `AA`, `AANord`, `DLC1`, `DLC2`, `zzz`), the `AA`/`ba` marker, piece suffixes (`Cuirass`, `Gauntlets`, `Boots`, `Helmet`, `Hood`, `Shield`, `Circlet`, `Plate`, `Robe`, ...), and stop words (`No`, `Yes`), then keeps alphanumerics, lowercases invariant, and produces a CamelCase Title-case `DisplayName`. If no meaningful middle remains, the ARMA mesh folder segment after `armor`/`clothes` (suffixes `male`/`female`/`_0`/`_1`/`_1st` stripped) is used.

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
- Real-game tests are `[Trait("Category","Integration")]` and skip automatically when the game folder is absent.

## Status

Scans are complete up to `VariantAssembler` (per-set `(Gender, Weight)` variants, unisex by design). Next: Sprint 1.5 (catalog model + cache), then goldens (Sprint 1.6) and the real vanilla tuning pass (Sprint 1.7).