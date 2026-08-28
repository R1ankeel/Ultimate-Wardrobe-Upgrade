# App - WPF Shell

> Phase 6 (Sprints 6.1-6.4 done) - `src/UltimateWardrobe.App` is the WPF desktop shell. `net10.0-windows`, `UseWPF=true`, CommunityToolkit.Mvvm ViewModels, WPF-UI 4.3.0 Fluent shell, Microsoft.Extensions.Hosting composition root, Serilog logging. This doc records the composition/host lifecycle, the startup gate, the WPF-UI 4.3.0 service wiring, the Sprint 6.1 spike conclusions, the Sprint 6.2 project/overhaul management, the Sprint 6.3 donor library and the Sprint 6.4 mapping matrix grid.

## Stack

- WPF-UI **4.3.0** (packages under `lib/net10.0-windows7.0/` - valid on `net10.0-windows`)
- CommunityToolkit.Mvvm 8.4.0, Microsoft.Extensions.Hosting 10.0.0, Microsoft.Extensions.Logging.Abstractions
- Serilog 4.2.0 + Serilog.Sinks.File 6.0.0 + Serilog.Extensions.Hosting 8.0.0

`UltimateWardrobe.App` is the only project referencing the UI/hosting packages; every `src/*` sibling stays frozen. The output is `WinExe` with `app.manifest` (PerMonitorV2 DPI). `Version` is `0.6.1`.

## Composition root - `CompositionRoot.Register(IServiceCollection services, bool registerUi = true)`

The single composition root is shared by the real app and the headless tests. `registerUi=true` adds the WPF-UI-backed adapters and Views; `registerUi=false` (headless, amendment 2) registers no-op stubs at the adapter interfaces so every ViewModel and app service resolves with no WPF control ever constructed.

Always registered:
- App services: `ILogViewer`/`LogViewer`, `RecentProjectsStore`, `IProjectSession`/`ProjectSession`, `IProjectStoreFactory`/`ProjectStoreFactory`, `IOverhaulSourceValidator`/`OverhaulSourceValidator`, `IBackgroundTaskService`/`DispatcherBackgroundTaskService`, `IOverhaulSelection`/`OverhaulSelection`.
- Domain services: `CompositeExtractor` / `IArchiveExtractor`, `FolderCatalogScanner`, `DonorClassifier`, `DonorImportService`, `DonorLibraryService`, `IPatcher`/`WardrobePatcher`, and `MappingService` (a factory that requires an open `IProjectSession` and constructs `new MappingService(session.Project.Library)`).
- ViewModels: `ProjectListViewModel`, `MainViewModel`, `ProjectViewModel`, `OverhaulViewModel`, `ArmorSetDetailViewModel`, `DonorLibraryViewModel`, `ExportViewModel`.

`registerUi=true` additionally registers the WPF-UI wiring (below) and the Views (`MainWindow`, `ProjectPickerWindow`, `ProjectView`, `OverhaulView`, `ExportView`). `registerUi=false` additionally registers `NullAppNavigationService`, `NullAppDialogService`, `NullSnackbarService`.

A guarded marker type makes a second `Register` call on the same `IServiceCollection` throw (`CompositionRoot.Register has already been called for this service collection.`).

## Host lifecycle - `App.OnStartup` / `App.OnExit`

`App` (a `System.Windows.Application`) builds a `Host.CreateApplicationBuilder()` on the UI thread in `OnStartup`, configures Serilog (file sink into `%LocalAppData%\UltimateWardrobe\logs\app-{Date}.log`, rolling day, retained 7, plus a `LogViewerSink` forwarding every rendered line into `ILogViewer`), calls `CompositionRoot.Register`, builds and starts the host, then runs the startup gate. `ShutdownMode` is `OnExplicitShutdown`. `_host.Dispose()` runs on `OnExit`.

## Startup gate - single project per process (amendment 7)

`App.OnStartup` resolves `ProjectPickerWindow` (hosting `ProjectListViewModel`) and shows it modally BEFORE the shell. A canceled/empty picker leaves `IProjectSession` closed and the app calls `Shutdown(0)` with no shell. On a successful pick/create `ProjectListViewModel.OpenRootAsync` opens the `project.db` via `IProjectStoreFactory`, publishes the single open project on `IProjectSession` (binding the shared `IProjectStore`), records it in `RecentProjectsStore`, and raises `CloseRequested` so the picker closes. "New project" genuinely creates `project.db` (fresh folder) and "Open project" loads an existing one; opening a folder without `project.db` in "Open" mode alerts and leaves the session untouched. `App` then resolves and shows `MainWindow` scoped to that one project for the process lifetime. There is no in-app "switch/close project" command - relaunch to change projects. `ProjectListViewModel` stays headless-testable (hosted by the picker, not a navigation page).

## WPF-UI 4.3.0 service wiring (the Sprint 6.1 spike result)

The exact wiring is resolved against the shipped 4.3.0 API and verified by a headless STA boot check (FluentWindow shown, Loaded fires, page provider attached, programmatic navigation to `ProjectView` succeeds, no exception):

1. `Wpf.Ui.Abstractions.INavigationViewPageProvider` - WPF-UI 4.x RENAMED the planner's "IPageService" to this abstraction. Own implementation `AppNavigationViewPageProvider` resolves page instances from the composition root: `object GetPage(Type pageType) => _services.GetRequiredService(pageType)`.
2. `Wpf.Ui.NavigationService` is registered in DI over that page provider and resolved as `Wpf.Ui.INavigationService`: `new Wpf.Ui.NavigationService(pageProvider)` (its ctor takes the `INavigationViewPageProvider`).
3. In `MainWindow.OnLoaded` (surface method `InitializeNavigation`): `RootNavigation.SetPageProviderService(_pageProvider)` attaches the DI page provider to the `NavigationView` so item clicks resolve pages from DI, and `_navigationService.SetNavigationControl(RootNavigation)` binds the programmatic service to the same control. `RootNavigation.Navigate(typeof(ProjectView))` lands the first screen. The window's class doc for the shell navigation: `NavigationView` items (`NavigationViewItem`) declare `TargetPageType` and navigate through the page provider.
4. `IAppDialogService` (WPF-UI-backed `WpfUiDialogService`, null-stubbed `NullAppDialogService` headless): folder picking uses `Microsoft.Win32.OpenFolderDialog` (`PickFolderAsync`/`PickProjectFolderAsync`) - WPF-UI 4.3 ships no folder picker (spike conclusion); `PromptTextAsync` uses a modal `Window` with a `TextBox` because WPF-UI 4.3 has NO `TextBoxContentDialog`; confirm/alert use the WPF-UI `ContentDialog` host when attached, else `MessageBox`.

## Spike conclusions (recorded for maintainers)

- **Symbol set**: `Wpf.Ui.Controls.SymbolRegular` has NO plain `Cancel` / `Stop` / `Dismiss` / `ChromeClose` member. Using `Cancel24` throws at XAML parse (`XamlParseException: Cancel24 is not a valid value for SymbolRegular`). Valid members used: `Home24`, `GridDots24`, `Save24`, `Add24`, `FolderOpen24`. Avoid non-existent symbols - they fail at runtime, not compile.
- **No `ui:Page` control in 4.3**: WPF-UI removed/renamed the `Page` control (compiler `MC3074: tag "Page" does not exist`). Placeholder pages inherit standard `System.Windows.Controls.Page`; do NOT use `<ui:Page>`.
- **Themes**: theme dictionaries live in the `Wpf.Ui.Markup` namespace - `ThemesDictionary` (`Theme="Dark"`, dark default) and `ControlsDictionary`, merged in `App.xaml`. WPF `Page` has no `Padding` property (use a margin on the content).
- **`System.IO` is NOT an implicit using for `UseWPF=true` SDK projects** (WPF implicit usings omit `System.IO`); add `using System.IO;` explicitly wherever `Path`/`File`/`Directory`/`IOException` are used.
- **`NavigationView.Navigate` requires the applied window template** (content presenter). In the real app `OnLoaded` handles this; in a headless test you must `Show()` + pump the dispatcher (or `ApplyTemplate` + layout) or it NREs inside `UpdateContent`.
- **MessageBox ambiguity**: with both `using System.Windows;` and `using Wpf.Ui.Controls;`, `MessageBox`/`MessageBoxButton`/`MessageBoxResult` are ambiguous - alias them to `System.Windows.*`.
- **`NoWarn` scoping**: NU package-version warnings (`NU1900..NU1904`) are throttled in `UltimateWardrobe.App.csproj` only (`NoWarn` + `WarningsNotAsErrors`). The zero-warnings Release gate still holds for the whole solution. The App project also emits XAML/template warnings that are absorbed by the build.

## Sprint 6.2 - project + overhaul management

### Project picker (`ProjectListViewModel`, hosted by `ProjectPickerWindow`)

- Recent projects from `RecentProjectsStore` (`settings.json`), top-8 newest-first with dedup; per-item remove through `RemoveRecentCommand`.
- "New project" -> pick a folder (`PickProjectFolderAsync`) -> `ProjectStoreFactory.Open` -> creates `project.db`, saves a fresh `Project`, records recent, opens the session, raises `CloseRequested`.
- "Open project" -> pick a folder -> loads the existing `project.db` through `IProjectStore.LoadAsync`; a missing `project.db` in Open mode alerts and does nothing.
- A canceled folder dialog is a no-op. Every failure surfaces as an alert and leaves the session untouched.
- NO "switch/close project" command exists in the picker or any view model (a reflection `CommandNameLeak` guard enforces it) - relaunch to change projects.

### Overhaul cards (`ProjectViewModel`, `ProjectView`)

- Cards are immutable snapshots derived from the session graph + `MappingService.GetOverhaulProgress`: name, `DoneFraction`, mapped/total counts (Mapped + Done), status label (No catalog - run a scan / Not started / In progress / Complete), plus the living `Overhaul` reference.
- Add Vanilla: pick game root -> `IOverhaulSourceValidator.ValidateVanilla` (requires `Data\Skyrim.esm`) -> `VanillaCatalogSource`.
- Add StoryMod: pick game root + mod root + prompt main plugin -> `ValidateStoryMod` (vanilla base + main plugin + masters beside it) -> `StoryModCatalogSource`.
- Per-card Rename (reconstructs the immutable `Overhaul`, sets `ModifiedAt`) / Delete (confirmation -> removes from the graph -> autosave) / Select (navigates to `OverhaulView`).
- Cards refresh on `Page.Loaded` (not `OnNavigatedTo` - not overridable on `Page`).

### Persistence / autosave (amendment 3)

`IProjectSession.Store` is the ONE `IProjectStore` bound to the opened `project.db`, opened once by the picker via `IProjectStoreFactory` and shared by every view model so autosave never races two stores on the same file. Every mutation (add/rename/delete overhaul) flushes through it.

**Frozen Phases 1-5 constraint (documented limitation):** `ProjectStore.SaveAsync` is upsert-only - deleting an Overhaul removes it from the live graph and autosaves, but the real `Overhaul`/`Mapping` DB rows remain (no orphan/row GC). This is a Phase 4 limitation deliberately NOT touched here.

## Donor library screen (Sprint 6.3)

`DonorLibraryView` (`DonorLibraryViewModel`) shows the open project's `DonorLibrary.Assets` as a table: original file name, Kind badge (roadmap 4.3), ProvidedSets count, BodySlide/physics indicators and import date, with per-row Remove / Reclassify / Set Kind commands. Import is a drop zone (`.7z` / `.zip` / `.rar`, multi-file, folders flattened) that runs the Phase 2 pipeline one archive at a time on `IBackgroundTaskService` through `IDonorImportRunner` with a per-file `ProgressBar` + cancel. A failed archive surfaces a typed alert and adds nothing (Phase 2 already cleaned up); a cancelled batch is silent. Every mutation autosaves through the shared `IProjectSession.Store`. The concrete sealed `DonorLibraryService` is injected directly (consistent with `ProjectViewModel` -> `MappingService`) and the per-file runner abstraction keeps the view model headless-testable.

## Mapping matrix grid (Sprint 6.4)

`OverhaulView` (`OverhaulViewModel`) is the per-Overhaul mapping matrix over `Overhaul.Catalog` sets (amendment 8). Because the page is recreated from DI on each navigation (via `AppNavigationViewPageProvider`), the selected Overhaul cannot live on the page: the App-layer singleton `IOverhaulSelection`/`OverhaulSelection` holds the `OverhaulId` - `ProjectViewModel.SelectOverhaul` sets it and `OverhaulViewModel` resolves the Overhaul from `_session.Project.Overhauls` by id.

- **Sections and rows.** FEMALE ARMOR then MALE ARMOR; a set with a Female/Unisex variant appears under FEMALE, a Male/Unisex variant under MALE (catalog order kept inside). Rows are `IReadOnlyList` projections (`ArmorSetRowViewModel`), never an `ObservableCollection` matrix rebuild.
- **Columns.** One `MatrixColumnViewModel` per distinct weight class present in the catalog (Heavy / Light / Clothing / n/a order); a missing weight class yields no column.
- **Cells.** One `MatrixCellViewModel` per (set, gender, weight). A missing variant or an unmapped variant renders blank/empty (`IsBlank`); a mapped variant raises a card (`CellLineRole`): set name, one line per distinct base donor, then one per `BodyConversionPatch`/`PhysicsPatch` (mirrors the wireframe "ARMOR 1 / LOAD ARMOR / PATCH / PATCH").
- **Status.** Set status via `MappingService.GetArmorSetStatus`; the header label via `GetOverhaulProgress`. A status filter (`StatusFilter`/`SelectedStatusOption`) highlights matching rows (`IsStatusMatch`).
- **Search.** `SearchText` filters the row band in both sections case-insensitively; a section with no matching rows is dropped.
- **Popover wiring (Sprint 6.5).** `CellAt(sectionIndex,rowIndex,columnIndex)` resolves a cell's coordinates and `Activate`/`ActivateCellCommand` feed `ActiveCell` - the anchored popover anchors to that cell.
- **Empty state.** A null/uncached catalog shows `IsEmpty` with the "run a scan first" hint.
- **Virtualization.** Projection-shaped (rows are `IReadOnlyList`) and view-shaped (`VirtualizingStackPanel` row bands + horizontal cell `ItemsControl`, fixed header row + frozen set-name column). `MatrixCellViewModel.IsBlank` hides the cell card.

Note: `DonorLibrary` is also a namespace name, so the `UltimateWardrobe.Core.Domain.DonorLibrary` type is fully qualified wherever the `UltimateWardrobe.DonorLibrary` namespace is imported.

## Screen / navigation map (roadmap 8.2, minus a shell project list)

```
App.OnStartup
  ProjectPickerWindow  - recent projects, "New project", "Open project" (ProjectListViewModel)

MainWindow (FluentWindow)
  NavigationView pane
    Project       - overhaul cards: name, progress, mapped/total, status + Add (Vanilla/StoryMod) + rename/delete/select (Sprint 6.2)
    Overhaul      - mapping matrix grid (FEMALE/MALE ARMOR sections x weight columns, cell cards, search/status filter; popover editor in 6.5) (Sprint 6.4)
    Donor library - donor table (Kind badge, sets, BodySlide/physics, date) + drag-and-drop import drop zone (Sprint 6.3)
    Export        - placeholder (Sprint 6.6: checklist + build + result)
  Status bar       busy spinner (IsBusy), latest ILogViewer line, Cancel, version in title
```

The status bar is bound to `MainViewModel` (busy spinner visibility to `IsBusy`, live text to `StatusText` fed by `ILogViewer.LineAppended`, `CancelCommand`, version in `Title`). The Export screen is a placeholder until Sprint 6.6; the Project screen (overhaul cards) landed in Sprint 6.2, the Donor library screen in Sprint 6.3 and the Overhaul matrix in Sprint 6.4.

## Settings layout

Repository-owned data lives under the user's Project root (`project.db`, `Source/`, `CatalogCache/`, `Export/`, `logs/`). App-level settings (recent projects, last theme) live under `%LocalAppData%\UltimateWardrobe\` - `RecentProjectsStore` reads/writes `settings.json` there (top-8 recent `project.db` paths, newest first, dedup, corrupt file -> empty).

## MVVM conventions

`ObservableObject` partial classes with `[ObservableProperty]` / `[RelayCommand]` / `[AsyncRelayCommand]`, `[ObservableProperty]` collections, `AsyncRelayCommand` + `CanExecute` as the "busy" guard. ViewModels depend only on App-layer interfaces (`IAppNavigationService`, `IAppDialogService`, `ISnackbarService`, `IBackgroundTaskService`, `IOverhaulSourceValidator`, `ILogViewer`, `IDonorImportRunner`, `IOverhaulSelection`) so they run headless in xUnit; the WPF-UI-backed implementations live in `Views/Infrastructure`. Concrete Phase 2 services (`MappingService`, `DonorLibraryService`) are injected directly. The matrix's read-only projections (`MatrixColumnViewModel`, `MatrixSectionViewModel`, `ArmorSetRowViewModel`, `MatrixCellViewModel`, `CellLineViewModel`) are immutable `IReadOnlyList` snapshots, refreshed whole on `Refresh()` - no per-cell observable rebuild.

## Screens still to come

- Sprint 6.5 - `ArmorSetDetailViewModel` anchored-popover cell editor (anchored to the activated matrix cell).
- Sprint 6.6 - `ExportViewModel` checklist + `IPatcher` invocation + result card + polish + manual E2E.
