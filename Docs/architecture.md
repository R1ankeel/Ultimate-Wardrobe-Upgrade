# Architecture

## Overview

UltimateWardrobe is a standalone Windows desktop application (.NET 10 LTS, WPF) that converts donor mod archives into a single MO2-ready replacer without touching load order or game balance. Only the visual layer (ARMA / mesh / texture / physics / BodySlide) is replaced.

## Solution Layout

```
UltimateWardrobe.sln
├── src/UltimateWardrobe.Core        # Domain POCOs + enums + invariants + abstractions (no I/O)
├── src/UltimateWardrobe.Archives    # P/Invoke over 7z.dll / UnRAR64.dll, signature detection, recursion├── src/UltimateWardrobe.Scanner     # Mutagen.Bethesda.Skyrim 0.54.4 folder catalog scanner (Sprint 1.2: plugin discovery + load order + record index + ARMO->ARMA->files correlation)
├── src/UltimateWardrobe.DonorLibrary# Phase 2 donor classification + service (branches 1-3 + import flow, Sprints 2.0-2.5 done)
├── src/UltimateWardrobe.Mapping     # (Phase 3 done) manual mapping + patch detection + status/progress (Sprints 3.0-3.4 done - CRUD/validation + patch detection + set status/progress + determinism + real-donor Integration spot-check; Core 3.0.2 amendment: PatchPolicy + Overhaul.Policy)
├── src/UltimateWardrobe.Persistence # Phase 4 SQLite (Phase 4 done, Sprints 4.0-4.4 - project + Microsoft.Data.Sqlite + ProjectDatabase WAL/foreign_keys bootstrap + Migrator/M001_Initial schema + .bak backup + UnitOfWork re-issuing PRAGMA defer_foreign_keys=ON per transaction + RowCodecs + 5 repositories + PersistenceJson row codecs + IProjectStore.SaveAsync/LoadAsync whole-graph round-trip + Integration real-donor spot-check; Core 4.0.2 amendment: Overhaul.CreatedAt/ModifiedAt; Core 4.3 amendment: Overhaul.Catalog)
├── src/UltimateWardrobe.Patcher     # Phase 5 ESP patcher + file slicer + orchestrator (Phase 5 done, Sprints 5.0-5.4 - Core IPatcher amendment + TargetResolver + PatchException + PluginBuilder override records/ESL gate/loose-path skip + DonorFileLocator + FileSlicer whitelist slice/overlay/dedup + OutputFolder mod folder/meta.ini + WardrobePatcher orchestrator + real-data Integration spot-check + xEdit validation proxy; docs in Docs/patcher.md)
├── src/UltimateWardrobe.App         # (Phase 6 - WPF shell) generic-host composition root + ProjectPickerWindow startup gate (amendment 7) + FluentWindow/NavigationView shell + StatusBar + Project/Overhaul screens + donor library drop-zone screen + mapping matrix grid + anchored-popover cell editor + set-level replacement editor (Sprints 6.1-6.10 done in docs/plans: DI smoke green + picker/create/open + overhaul cards + donor table + drag-and-drop import via IDonorImportRunner + the per-Overhaul FEMALE/MALE matrix over weight columns with cell cards/blank cells/2-D virtualization + the single-cell editor driving MappingService from an anchored popover + the set-level replacement editor with Load-Armor archive picker + the export screen running the Phase 5 IPatcher + dark/light theme toggle + status-glyph legend + app icon; WPF-UI 4.3.0 wiring + spike in Docs/app.md. Post-6.6 hotfixes: (1) the app-scope AppIcon is applied from code because a root-element StaticResource cannot resolve the window's own resources - the parse-time failure crashed the shell right after the picker created/opened a project (regression-test `MainWindowBootTests`); (2) the Overhaul page crashed on open because `Margin="Auto,0,0,0"` on the popover "Import patch" button is an invalid Thickness - the page failed to parse when navigation built it (regression-test `OverhaulViewBootTests`). Post-6.9 user-fix pass (Sprint 6.10): recent projects reopen with a recreated missing DB, the editor's "Load Armor" picks + imports + assigns a donor archive, and the scanner's word-boundary Ench-token rule drops DLC-prefixed enchanted variants)
└── tests/UltimateWardrobe.Tests     # xUnit + FluentAssertions
```

Dependency rule: `Core` depends on nothing. All other `src/*` depend only on `Core` (+ their own external lib). `App` depends on all.

## Key Abstractions (Phase 0)

```csharp
interface IArchiveExtractor { Task<ExtractResult> ExtractAsync(string archivePath, string destDir, IProgress<ExtractProgress>? progress, CancellationToken ct); }
interface ICatalogScanner   { Task<Catalog> ScanAsync(CatalogSource source, CancellationToken ct); }
interface IDonorClassifier  { Task<DonorAsset> ClassifyAsync(string extractedDir, Catalog catalogHint, CancellationToken ct); }
interface IProjectStore     { Task SaveAsync(Project project); Task<Project> LoadAsync(string projectDbPath); }
interface IPatcher          { Task<PatchResult> BuildAsync(Overhaul overhaul, DonorLibrary donorLibrary, string outputDir, IProgress<PatchProgress>? progress, CancellationToken ct); }
```

## Runtime Requirements

- Windows x64, .NET 10 SDK 10.0.100+
- Native `runtimes/win-x64/native/7z.dll` + `UnRAR64.dll` shipped alongside the app

## Build Properties

Centralized in `Directory.Build.props`:

- `TargetFramework = net10.0-windows`
- `LangVersion = 13`, `Nullable = enable`, `ImplicitUsings = enable`, `TreatWarningsAsErrors = true`

Per-project `global.json` pins SDK to `10.0.100` (`rollForward: latestFeature`).

## Future Docs

- `Docs/domain-model.md` - domain invariants and fixtures (Sprint 0.1)
- `Docs/archive-layer.md` - P/Invoke details, recursion, safety (Sprint 0.2)
- `Docs/scanner.md` - folder catalog scanner (Phase 1, Sprints 1.0-1.7)
- `Docs/donor-library.md` - donor import + graduated classification (Phase 2, Sprints 2.0-2.5 done)
- `Docs/mapping.md` - manual mapping + patch detection + status/progress + determinism + real-donor spot-check (Phase 3, Sprints 3.0-3.4 done)
- `Docs/persistence.md` - SQLite project DB: schema, migrations + backup, repositories/`UnitOfWork`/`IProjectStore`, JSON serialization, test strategy (Phase 4, Sprints 4.0-4.4 done)
- `Docs/patcher.md` - ESP patcher + target resolution + plugin builder + file slicer + orchestrator (Phase 5 done, Sprints 5.0-5.4 - `TargetResolver` + Core `IPatcher`/`PatchReport` amendment + `PluginBuilder` override records/ESL gate/amendment #6 loose-path skip + `DonorFileLocator` + `FileSlicer` whitelist slice/patch overlay/whole-export dedup + `OutputFolder` mod folder/meta.ini + `WardrobePatcher` orchestrator + real-data Integration spot-check + xEdit validation proxy)
- `Plans/phase5.md` - Phase 5 plan, Sprints 5.0-5.4 done (Scaffolding + Core `IPatcher` amendment + `TargetResolver` + `PluginBuilder` + `FileSlicer`/`DonorFileLocator` + `OutputFolder`/`WardrobePatcher` + real-data Integration + xEdit proxy + DoD)
- `Docs/app.md` - WPF shell: composition root + host lifecycle + startup `ProjectPickerWindow` gate (single project per process, amendment 7) + project/overhaul management (picker create/open/recent, recent-open DB recreation, overhaul cards, shared-store autosave) + donor library screen (table + drop-zone import via `IDonorImportRunner`) + the mapping matrix grid (FEMALE/MALE sections x weight columns, cell cards, 2-D virtualization, `IOverhaulSelection`) + the anchored-popover cell editor (`ArmorSetDetailViewModel` rescoped, autosave on edit + flush on close) + the set-level replacement editor (ARMOR 1 inventory / ARMOR 2 donor + body/physics patch rows + Load-Armor archive picker) + the export screen (checklist + `IPatcher` on `IBackgroundTaskService` + progress/cancel + result card + re-export) + dark/light theme toggle + status-glyph legend + WPF-UI 4.3.0 wiring + spike conclusions (Phase 6, Sprints 6.1-6.10 done)
- `Docs/domain-model.md` - domain invariants and fixtures (Sprint 0.1)

- `Plans/phase6.md` - Phase 6 plan (scaffolding/composition root/shell + Project/Overhaul CRUD + donor import + mapping matrix grid + anchored-popover editor + export/polish/E2E + UI hardening + scan-time filters + 6.9 scan/filter/editor tuning + 6.10 user-fix pass + DoD; Sprints 6.1-6.10 done)