# UltimateWardrobe

Ultimate Wardrobe Upgrade - Product Description

What It Is

UltimateWardrobe is a standalone application for creating complete visual replacers for armor and clothing in Skyrim SE without requiring access to the user's actual load order.

The application does not "patch the game" in the traditional sense of modding tools. Instead, it turns a collection of separate donor mod archives - which would normally have to be manually extracted, sorted, and adapted for BodySlide and physics - into a single, cohesive replacer mod ready for installation through Mod Organizer 2.

The product addresses a specific and familiar problem for anyone who has built a large modlist: the desire to give the entire game a consistent visual style, for example, having all armor use 3BA/HIMBO with consistent physics, without spending months manually dealing with xEdit, conflicting patches, and separate BodySlide presets for every individual armor set.

Who It Is For

The tool is intended for users who:

Build large modlists and want a consistent visual style for all armor and clothing in the game.
Maintain several independent builds in parallel, for example, different PCs with different visual concepts, and do not want work on one build to interfere with another.
Want to distribute the result as a ready-to-use replacer mod for other people's builds without tying it to gameplay balance or level lists - meaning Iron Armor remains completely vanilla in terms of its gameplay properties and simply looks different.
How It Works - User Workflow

The user creates a Project - a workspace for one independent build.

Each Project has its own internal library of imported donor mods, completely isolated from other Projects. This makes it possible to maintain several completely independent visual concepts in parallel without mixing or cross-contaminating them.

Inside a Project, the user creates one or more Overhauls - each targeting a specific armor source: vanilla Skyrim + official DLCs, or a story expansion such as Vigilant or Lordbound.

All the user needs to do is provide the application with a folder - either the installed Skyrim directory or an extracted story mod - and the application automatically analyzes all armor and clothing it contains, organizing them into recognizable sets such as Iron Armor, Vigilant's Cuirass, and so on, while also separating them by gender and weight categories.

The user then simply downloads donor mods - donor archives containing, for example, a specific replacement for Iron Armor - and adds them to the application.

UltimateWardrobe automatically extracts the archive, analyzes its contents, and organizes the discovered armor according to the same principles used for the original source armor.

The user then manually maps the items: "this is the vanilla Iron Armor for the male model - this is the donor set that should replace it." The same mapping can be configured separately for the female model if the donor differs between genders.

If the donor already contains ready-made BodySlide presets and physics support, the application detects and uses them automatically. If something is missing, it asks the user to provide the required patch, such as a conversion for the target body or physics system. That patch then becomes part of the same mapping.

The user works through all armor sets in an Overhaul at their own pace - however many they can complete in one session.

Progress is saved continuously, so a large build containing hundreds of armor sets can be completed incrementally: save the project, close the application, return the next day, and continue exactly where you left off, with a clear understanding of what has already been completed and what remains.

Once an Overhaul is fully configured, the user can press a single button to build the final mod.

The application extracts only the files actually used from the donor mods, excludes everything unnecessary, and packages the result into a ready-to-install mod structure.

Each Overhaul within a Project is exported as a separate standalone mod. For example, a vanilla armor replacer and a Vigilant armor replacer are not merged into a single monolithic All-in-One package. They remain separate mods that the user can install together or independently.

Core Product Principles
Complete Independence from the Game and Its Load Order

The application does not need access to the user's actual load order, plugin manager, or running game. It only needs a path to the relevant folder on disk.

This makes the tool simple, predictable, and safe to use. It cannot break an existing build because it does not directly interact with it in the first place.

Visual Layer Only - Zero Risk to Gameplay Balance

The replacer does not create new items, modify armor stats, touch keywords, weight, or item classification.

Iron Armor remains the exact same Iron Armor as in the original game - with all of its crafting recipes, level-list entries, and compatibility with any balance mods.

The only things that change are how the armor looks and how it physically behaves on the character's body.

Separate, Self-Contained Results

Each Overhaul is a separate, independently installable mod.

The user does not have to build one enormous AIO package. A vanilla armor replacer, a Vigilant armor replacer, and a Lordbound armor replacer are three separate mods that can be combined however the user wants.

Designed for Large-Scale Work

A complete visual overhaul of the game means working with hundreds of armor sets, which is not something that can realistically be completed in a single sitting.

The product is designed around this reality: progress is saved continuously, the status of every armor set is visible at any time, and unfinished work can be resumed as many times as necessary.

Extensibility Without Reworking the Foundation

Although the first complete version of the product focuses on armor and clothing for vanilla Skyrim and its official DLCs, the architecture is designed from the beginning to support the same workflow for major story expansions.

Adding support for projects such as Vigilant or Lordbound does not require fundamental changes to how the tool works.

Standalone application for building full visual replacers for Skyrim SE armor and clothing - without requiring connection to the user's real load order.

## Status

- Phase 0 - Foundation: done
  - Sprint 0.0 scaffolding - done (solution builds on .NET 10, native DLLs wired)
  - Sprint 0.1 domain model - done (`UltimateWardrobe.Core` with POCOs/enums/abstractions, 47 tests green)
  - Sprint 0.2 archive layer - done (`UltimateWardrobe.Archives` native-first over 7z.dll (7z/zip) + UnRAR64.dll (rar) with SharpCompress fallback, 96 tests green)
- Phase 1 - Folder Catalog Scanner (Mutagen): in progress
  - Sprint 1.0 scaffolding + spike - done (`UltimateWardrobe.Scanner` scaffolded, real `Skyrim.esm` read path proven via overlay, spike conclusions + Slot/gender-signal freezes recorded in `Plans/phase1.md`)
  - Sprint 1.1 plugin loading + RecordIndex - done (`PluginDiscovery`, `LoadOrderBuilder`, `ModLoader`, `RecordIndex`; synthetic pair maps a main-plugin FormID to the override record, corrupt plugin does not abort the scan; 121 tests green)
  - Sprint 1.2 ARMO -> ARMA -> files correlation - done (`FileResolver` path matrix, `ArmorCorrelator` pre-Piece with mesh/texture paths from TXST, unresolved-link/missing-master warnings never abort; 134 tests green)

## Stack

- .NET 10 LTS (`net10.0-windows`), C# 13
- WPF (MVVM, CommunityToolkit.Mvvm) - from Phase 6
- Mutagen.Bethesda.Skyrim 0.54.4 - Phase 1 (Scanner)
- SQLite + Microsoft.Data.Sqlite - from Phase 4
- Archives extracted natively via `7z.dll` (7z/zip) + `UnRAR64.dll` (rar), SharpCompress fallback - Phase 0.2

## Solution

```
UltimateWardrobe.slnx
├── src/UltimateWardrobe.Core        # Domain model, no I/O
├── src/UltimateWardrobe.Archives    # Archive extraction (native first)
├── src/UltimateWardrobe.Scanner     # Mutagen folder catalog scanner (Phase 1)
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
- `Plans/phase0.md`, `Plans/phase1.md` - implementation plans (phase 1 in progress)
- `Docs/architecture.md` - architecture overview
- `Docs/domain-model.md` - domain model (Sprint 0.1 - done)
- `Docs/archive-layer.md` - archive layer (Sprint 0.2 - done)

## Test Assets

Real mod archives for manual integration testing are under `ModsForTests/` (gitignored outputs, never committed). Small synthetic goldens are under `tests/TestData/Archives/`.

Game ESMs for reference: `D:\Skymod\Stock Game` (read-only).
