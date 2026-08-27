# UltimateWardrobe

Standalone application for building full visual replacers for Skyrim SE armor and clothing - without requiring connection to the user's real load order.

## Status

- Phase 0 - Foundation: in progress
  - Sprint 0.0 scaffolding - done (solution builds on .NET 10, native DLLs wired)
  - Sprint 0.1 domain model - done (`UltimateWardrobe.Core` with POCOs/enums/abstractions, 47 tests green)
  - Sprint 0.2 archive layer - done (`UltimateWardrobe.Archives` native-first over 7z.dll (7z/zip) + UnRAR64.dll (rar) with SharpCompress fallback, 96 tests green)

## Stack

- .NET 10 LTS (`net10.0-windows`), C# 13
- WPF (MVVM, CommunityToolkit.Mvvm) - from Phase 6
- Mutagen.Bethesda - from Phase 1
- SQLite + Microsoft.Data.Sqlite - from Phase 4
- Archives extracted natively via `7z.dll` (7z/zip) + `UnRAR64.dll` (rar), SharpCompress fallback - Phase 0.2

## Solution

```
UltimateWardrobe.slnx
├── src/UltimateWardrobe.Core        # Domain model, no I/O
├── src/UltimateWardrobe.Archives    # Archive extraction (native first)
└── tests/UltimateWardrobe.Tests     # xUnit + FluentAssertions
```

Native binaries are under `runtimes/win-x64/native/` and copied to output as `Content`.

## Build

```powershell
dotnet --version  # must be 10.x
dotnet build -c Release
dotnet test
```

## Docs

- `Plans/final-roadmap.md` - full roadmap (Phases 0-7)
- `Plans/phase0.md` - Phase 0 implementation plan (sprints)
- `Docs/architecture.md` - architecture overview
- `Docs/domain-model.md` - domain model (Sprint 0.1 - done)
- `Docs/archive-layer.md` - archive layer (Sprint 0.2 - done)

## Test Assets

Real mod archives for manual integration testing are under `ModsForTests/` (gitignored outputs, never committed). Small synthetic goldens are under `tests/TestData/Archives/`.

Game ESMs for reference: `D:\Skymod\Stock Game` (read-only).
