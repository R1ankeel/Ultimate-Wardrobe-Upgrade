# Architecture

## Overview

UltimateWardrobe is a standalone Windows desktop application (.NET 10 LTS, WPF) that converts donor mod archives into a single MO2-ready replacer without touching load order or game balance. Only the visual layer (ARMA / mesh / texture / physics / BodySlide) is replaced.

## Solution Layout

```
UltimateWardrobe.sln
├── src/UltimateWardrobe.Core        # Domain POCOs + enums + invariants + abstractions (no I/O)
├── src/UltimateWardrobe.Archives    # P/Invoke over 7z.dll / UnRAR64.dll, signature detection, recursion
├── src/UltimateWardrobe.Scanner     # Mutagen.Bethesda.Skyrim 0.54.4 folder catalog scanner (Sprint 1.2: plugin discovery + load order + record index + ARMO->ARMA->files correlation)
├── src/UltimateWardrobe.DonorLibrary# Phase 2 donor classification + service (branches 1-3 + import flow, Sprints 2.0-2.5 done)
├── src/UltimateWardrobe.Mapping     # (Phase 3) manual mapping + patch detection + status/progress (Sprints 3.0-3.3 done - CRUD/validation + patch detection + set status/progress; Core 3.0.2 amendment: PatchPolicy + Overhaul.Policy)
├── src/UltimateWardrobe.Persistence # (Phase 4) SQLite
├── src/UltimateWardrobe.Patcher     # (Phase 5) ESP patcher + file slicer
├── src/UltimateWardrobe.App         # (Phase 6) WPF
└── tests/UltimateWardrobe.Tests     # xUnit + FluentAssertions
```

Dependency rule: `Core` depends on nothing. All other `src/*` depend only on `Core` (+ their own external lib). `App` depends on all.

## Key Abstractions (Phase 0)

```csharp
interface IArchiveExtractor { Task<ExtractResult> ExtractAsync(string archivePath, string destDir, CancellationToken ct); }
interface ICatalogScanner   { Task<Catalog> ScanAsync(CatalogSource source, CancellationToken ct); }
interface IDonorClassifier  { Task<DonorAsset> ClassifyAsync(string extractedDir, Catalog catalogHint, CancellationToken ct); }
interface IProjectStore     { Task SaveAsync(Project project); Task<Project> LoadAsync(string projectDbPath); }
interface IPatcher          { Task<PatchResult> BuildAsync(Overhaul overhaul, string outputDir, CancellationToken ct); }
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
- `Docs/mapping.md` - manual mapping + patch detection + status derivation (Phase 3, Sprints 3.0-3.4)
