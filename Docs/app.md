# App - WPF Shell

> Phase 6 (Sprints 6.1-6.10 done) plus Optimization Phases A-E done - `src/UltimateWardrobe.App` is the WPF desktop shell. `net10.0-windows`, `UseWPF=true`, CommunityToolkit.Mvvm ViewModels, WPF-UI 4.3.0 Fluent shell, Microsoft.Extensions.Hosting composition root, Serilog logging. This doc records the composition/host lifecycle, the startup gate, the WPF-UI 4.3.0 service wiring, the Sprint 6.1 spike conclusions, the Sprint 6.2 project/overhaul management, the Sprint 6.3 donor library, the Sprint 6.4 mapping matrix grid, the Sprint 6.5 anchored-popover cell editor, the Sprint 6.8 matrix performance/interaction fixes, and Optimization Phases A (debounce/offload/cancel) + B (virtualization fix) + C (algorithmic rewrite) + D (prefilter polish) + E (benchmark/regression/virtualization smoke).

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

`App.OnStartup` resolves `ProjectPickerWindow` (hosting `ProjectListViewModel`) and shows it modally BEFORE the shell. A canceled/empty picker leaves `IProjectSession` closed and the app calls `Shutdown(0)` with no shell. On a successful pick/create `ProjectListViewModel.OpenRootAsync` opens the `project.db` via `IProjectStoreFactory`, publishes the single open project on `IProjectSession` (binding the shared `IProjectStore`), records it in `RecentProjectsStore`, and raises `CloseRequested` so the picker closes. "New project" genuinely creates `project.db` (fresh folder) and "Open project" loads an existing one; opening a folder without `project.db` in "Open" mode alerts and leaves the session untouched. A RECENT project reopens from its stored `project.db` path with `createIfMissing: true` - `OpenSelectedRecentCommand` derives the project folder via `Path.GetDirectoryName` (a bare `project.db` path must NOT be passed to `OpenRootAsync`, which itself appends `project.db`) and recreates the database when it is missing, so a deleted/corrupt DB no longer blocks reopening a recent project. `App` then resolves and shows `MainWindow` scoped to that one project for the process lifetime. There is no in-app "switch/close project" command - relaunch to change projects. `ProjectListViewModel` stays headless-testable (hosted by the picker, not a navigation page).

## WPF-UI 4.3.0 service wiring (the Sprint 6.1 spike result)

The exact wiring is resolved against the shipped 4.3.0 API and verified by a headless STA boot check (FluentWindow shown, Loaded fires, page provider attached, programmatic navigation to `ProjectView` succeeds, no exception):

1. `Wpf.Ui.Abstractions.INavigationViewPageProvider` - WPF-UI 4.x RENAMED the planner's "IPageService" to this abstraction. Own implementation `AppNavigationViewPageProvider` resolves page instances from the composition root: `object GetPage(Type pageType) => _services.GetRequiredService(pageType)`.
2. `Wpf.Ui.NavigationService` is registered in DI over that page provider and resolved as `Wpf.Ui.INavigationService`: `new Wpf.Ui.NavigationService(pageProvider)` (its ctor takes the `INavigationViewPageProvider`).
3. In `MainWindow.OnLoaded` (surface method `InitializeNavigation`): `RootNavigation.SetPageProviderService(_pageProvider)` attaches the DI page provider to the `NavigationView` so item clicks resolve pages from DI, and `_navigationService.SetNavigationControl(RootNavigation)` binds the programmatic service to the same control. `RootNavigation.Navigate(typeof(ProjectView))` lands the first screen. The window's class doc for the shell navigation: `NavigationView` items (`NavigationViewItem`) declare `TargetPageType` and navigate through the page provider.
4. `IAppDialogService` (WPF-UI-backed `WpfUiDialogService`, null-stubbed `NullAppDialogService` headless): folder picking uses `Microsoft.Win32.OpenFolderDialog` (`PickFolderAsync`/`PickProjectFolderAsync`) - WPF-UI 4.3 ships no folder picker (spike conclusion); mod-archive picking (`PickModArchiveAsync`, a Sprint 6.10 add for the editor's one-step "Load Armor") uses `Microsoft.Win32.OpenFileDialog` with the filter "Mod archives (*.7z;*.zip;*.rar)"; `PromptTextAsync` uses a modal `Window` with a `TextBox` because WPF-UI 4.3 has NO `TextBoxContentDialog`; confirm/alert use the WPF-UI `ContentDialog` host when attached, else `MessageBox`.

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
- "Recent project" -> double-click/reopen -> `OpenSelectedRecentCommand` derives the project folder from the stored `project.db` path (`Path.GetDirectoryName`) and calls `OpenRootAsync(folder, createIfMissing: true)`: the missing-DB alert applies only to the manual Open dialog, while a recent project with a lost `project.db` is recreated (the DB path itself was passed as the folder before the Sprint 6.10 fix, producing a bogus `...\project.db\project.db` path that blocked every recent open).
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
- **Virtualization.** Projection-shaped (rows are `IReadOnlyList`) and view-shaped (`VirtualizingStackPanel` row bands + horizontal cell `ItemsControl`, fixed header row + frozen set-name column). `MatrixCellViewModel.IsBlank` hides the cell card. The Sprint 6.8 pass replaced the row-band view shape with ONE flat virtualized `ItemsControl` over the flattened `MatrixItems` - see the Sprint 6.8 section below.

## Replacement editor (Sprint 6.5 anchored popover, reshaped to SET level in Sprint 6.9 T2)

`ArmorSetDetailViewModel` is the transient editor for the activated (set, gender, weight) `Variant` of the owning `Overhaul` + its `PieceMapping`s, hosted by an anchored WPF `Popup` (amendment 8). Since Sprint 6.9 T2 it edits the whole variant at SET level - one donor (plus optional patch layers) replaces every target piece at once, instead of per-piece actions:

- **Open/close state machine (on `OverhaulViewModel`).** `Activate(cell)` resolves the variant directly from `cell.Set` (the matrix cell is blank - `Variant` null - for an unmapped variant, so it cannot be the source of truth) then calls `CellEditor.Open(set, variant, overhaul, project.Library, project)`; `IsEditorOpen` + `ActiveCell` drive the popover anchor. Activating the already-open cell toggles it closed; `CloseEditor`/`FlushAndCloseEditorAsync` clear the editor (per-cell create + dispose-on-close, one shared `CellEditor` instance reused across cells). `Refresh()` clears the editor; `RecomputeMatrix()` re-projects the grid without touching an open editor.
- **Layout.** Two-column `Grid`: LEFT column "ARMOR 1" is a read-only piece inventory - one `PieceInventoryRowViewModel` per variant piece (EditorId, slot, target mesh, per-piece donor badges) - and RIGHT column "ARMOR 2" edits the single replacement. `AvailableDonors` (`FullReplacer` + compatible, current donor excluded) drives a ComboBox; `LoadDonorLabel` reads "Load Armor" (nothing loaded) or "Change" (`HasCurrentDonor`), `CurrentDonorText` reads "Armor: <mod name>" / "Nothing loaded yet". "Load Armor" now also works WITHOUT a ComboBox selection (Sprint 6.10): `LoadDonorCommand` is an `IAsyncRelayCommand` that assigns the selected donor when one is active, otherwise opens a mod-archive picker (`IAppDialogService.PickModArchiveAsync`, `.7z/.zip/.rar`), imports the archive through `IDonorImportRunner`, and assigns its `FullReplacer` donor in ONE step; an imported kind that is not a compatible `FullReplacer` alerts but keeps the asset in the donor library. The donor piece for each target piece is resolved via `DonorCompatibility.FindDonorPiece` and assigned through the Phase 3 `MappingService.AssignDonor` verbatim (a new donor REPLACES the variant's `PieceMapping`s, which clears stale patch layers automatically).
- **Body / physics checks (Sprint 6.9 T2).** Required body type is gender-driven - `DonorCompatibility.RequiredBodyTypeFor`: female -> 3BA, male -> HIMBO. `DonorContainsBody(donor, type)` is true when the donor owns BodySlide (`DetectedBodySlideFiles`) or a matching path marker (`BodyMarkerFromPath` 3ba/himbo tokens); `DonorHasPhysics(donor)` reads `DetectedPhysicsFiles`. Check lines render text status ("3BA: OK" / "HDT-SMP: OK" - plain text, no glyphs, per docs convention) when the donor already satisfies a check; otherwise a conditional dropdown row is offered ("Load 3BA patch"/"Load HDT-SMP patch") that picks ONE `BodyConversionPatch`/`PhysicsPatch` asset (`BodyPatches`/`PhysicsPatches`, seeded by `SelectPatchesForCurrentState`) attached set-level via `MappingService.AttachPatch`; an attached patch gets a Clear button (`ClearBodyPatchCommand`/`ClearPhysicsPatchCommand` -> `DetachPatch`).
- **Mutation.** The Phase 3 command set is kept verbatim over `MappingService`: `AssignDonor` / `AttachBodyPatch` / `AttachPhysicsPatch` / `DetachBodyPatch` / `DetachPhysicsPatch`. After each mutation the editor re-projects its rows + `SetStatus` and raises `Changed`.
- **Donor-library accounting (user-confirmed ruling).** Changing the donor re-assigns ONLY the current set. `UnloadDonorIfUnreferenced` unloads the replaced donor asset from `project.Library` only when NO mapping anywhere in `_project.Overhauls` still references it - as main donor (`DonorAssetId`) OR as an attached patch (`BodyConversionPatchAssetId`/`PhysicsPatchAssetId`); it stays while any other set/variant references it, and other sets are never touched.
- **Live refresh + autosave.** `OverhaulViewModel` observes `CellEditor.Changed`: it calls `RecomputeMatrix()` (the grid card lines re-project from `MappingService` results - no divergent state) and flushes `IProjectSession.Store.SaveAsync` (amendment 3 autosave) on every edit, with a guaranteed flush in `FlushAndCloseEditorAsync` when the popover closes.
- **View host.** `OverhaulView` hosts one `Popup` (`Placement=Bottom`, `StaysOpen=false` for outside-click/focus-loss; `PlacementTarget` set from the clicked cell's `Button` in `MatrixCell_Click` or the row-name button in `MatrixRow_Click`; page `PreviewKeyDown` Esc closes). The editor panel is a single shared DataTemplate reused across cells (MinWidth 440 / MaxWidth 640); an "Import patch" button (`ImportPatchCommand`) navigates to `DonorLibraryView`.

Note: `DonorLibrary` is also a namespace name, so the `UltimateWardrobe.Core.Domain.DonorLibrary` type is fully qualified wherever the `UltimateWardrobe.DonorLibrary` namespace is imported.

## Screen / navigation map (roadmap 8.2, minus a shell project list)

```
App.OnStartup
  ProjectPickerWindow  - recent projects, "New project", "Open project" (ProjectListViewModel)

MainWindow (FluentWindow)
  NavigationView pane
    Project       - overhaul cards: name, progress, mapped/total, status + Add (Vanilla/StoryMod) + rename/delete/select (Sprint 6.2)
    Overhaul      - mapping matrix grid (FEMALE/MALE ARMOR sections x weight columns, cell cards, search/status filter) + set-level replacement editor (Sprint 6.4 + 6.5, reshaped 6.9)
    Donor library - donor table (Kind badge, sets, BodySlide/physics, date) + drag-and-drop import drop zone (Sprint 6.3)
    Export        - pre-export checklist (status chip counts + allow-partial switch) + "Собрать гардероб" -> IPatcher on a background task + PatchProgress stages + Cancel + result card + open-in-Explorer + re-export (Sprint 6.6)
  Status bar       busy spinner (IsBusy), latest ILogViewer line, Cancel, version in title
```

The status bar is bound to `MainViewModel` (busy spinner visibility to `IsBusy`, live text to `StatusText` fed by `ILogViewer.LineAppended`, `CancelCommand`, version in `Title`). The Project screen (overhaul cards) landed in Sprint 6.2, the Donor library screen in Sprint 6.3, the Overhaul matrix in Sprint 6.4, the anchored-popover cell editor in Sprint 6.5 (reshaped into the set-level replacement editor in Sprint 6.9) and the Export screen in Sprint 6.6.

## Settings layout

Repository-owned data lives under the user's Project root (`project.db`, `Source/`, `CatalogCache/`, `Export/`, `logs/`). App-level settings (recent projects, last theme) live under `%LocalAppData%\UltimateWardrobe\` - `RecentProjectsStore` reads/writes `settings.json` there (top-8 recent `project.db` paths, newest first, dedup, corrupt file -> empty).

## MVVM conventions

`ObservableObject` partial classes with `[ObservableProperty]` / `[RelayCommand]` / `[AsyncRelayCommand]`, `[ObservableProperty]` collections, `AsyncRelayCommand` + `CanExecute` as the "busy" guard. ViewModels depend only on App-layer interfaces (`IAppNavigationService`, `IAppDialogService`, `ISnackbarService`, `IBackgroundTaskService`, `IOverhaulSourceValidator`, `ILogViewer`, `IDonorImportRunner`, `IOverhaulSelection`, `IThemeService`) so they run headless in xUnit; the WPF-UI-backed implementations live in `Views/Infrastructure`. Concrete Phase 2 services (`MappingService`, `DonorLibraryService`) are injected directly. The matrix's read-only projections (`MatrixColumnViewModel`, `MatrixSectionViewModel`, `MatrixSectionHeaderViewModel`, `ArmorSetRowViewModel`, `MatrixCellViewModel`, `CellLineViewModel`) are immutable `IReadOnlyList` snapshots refreshed whole on `Refresh()` - no per-cell observable rebuild. Those projections are flattened into the single `MatrixItems` list (section headers + rows) that drives the virtualized matrix body (Sprint 6.8).

## Export screen (Sprint 6.6)

`ExportViewModel` runs the EXISTING Phase 5 `IPatcher` (`WardrobePatcher`) unchanged - it never re-implements slicing/plugin-writing. State is built in `Refresh()` (called from `ExportView.Loaded`): an empty overhaul -> empty state; otherwise the pre-export checklist is rolled up from `MappingService.GetOverhaulProgress` into five status chip counts (`NotStarted` / `InProgress` / `NeedsPatch` / `Mapped` / `Done`), and `OutputFolder` defaults to `<Project.Root>/Export`. `AllowPartial` (a checkbox) switches between "require full Done" and "allow NeedsPatch + Done" gating.

"Собрать гардероб" (`BuildCommand`, an `AsyncRelayCommand` gated by `CanBuild`) runs `WardrobePatcher.BuildAsync(current, project.Library, outputFolder, Progress<PatchProgress>, cts.Token)` on `IBackgroundTaskService` (headless-safe: the `PatchResult` is captured via a closure variable because `RunAsync` returns `Task`, not `Task<T>`). `PatchProgress` stages render into the progress bar (`CurrentStage`, `CompletedStages`/`TotalStages`, `ProgressPercent`); a `PatchException`/any failure surfaces an `IAppDialogService.AlertAsync`; `OperationCanceledException` -> "Export cancelled" snackbar; success fills the result card from `PatchResult`/`PatchReport` (mod folder, plugin path, overridden records, copied files/bytes, `CopiedFiles`/`CopiedBytes`, warnings `ItemsControl`). `OpenInExplorerCommand` opens the mod folder via `Process.Start` with `UseShellExecute`. After the first successful build the primary button relabels to "Re-export"; the Phase 5 `OutputFolder.ClearModDir` contract makes re-export clean (delete-then-rebuild). `OutputFolder.ModName` = `UltimateWardrobe - <sanitized>`. Paths render in a monospace (`Consolas`) `TextBox`.

## Polish (Sprint 6.6)

- **Dark/light theme toggle, persisted.** `MainViewModel` exposes `IsDarkTheme`, `ThemeLabel` ("Dark"/"Light") and `ToggleThemeCommand` via the App-layer `IThemeService` abstraction (`App/Infrastructure`). UI impl `WpfUiThemeService` calls `ApplicationThemeManager.Apply` and persists to `RecentProjectsStore.SetThemeMode` (a `Theme` field in the same `%LocalAppData%\UltimateWardrobe\settings.json`; `GetThemeMode` defaults Dark and degrades on corrupt); headless impl `NullThemeService` is in-memory. `MainWindow` renders the toggle in `FooterMenuItems` with a `DarkTheme24` symbol.
- **Status glyph legend.** `OverhaulViewModel` exposes a static `StatusLegend` (`StatusLegendItem(Symbol, Label)`) - `CheckmarkCircle24` (Done), `GridDots24` (Mapped), `Warning24` (NeedsPatch), `Clock24` (InProgress), `Circle20` (NotStarted) - rendered in `OverhaulView`; the string->`SymbolRegular` conversion needs the `Views/StringToSymbolConverter` (`Enum.TryParse` with `Circle20` fallback) because `ui:SymbolIcon Symbol="{Binding Symbol}"` does not convert a string.
- **App icon** (vector `DrawingImage` wardrobe-door `AppIcon`, declared at app scope in `App.xaml`) + window title shows the app version. `MainWindow.ApplyAppIcon()` copies it from code - `Application.Current?.TryFindResource("AppIcon") as ImageSource`, null-safe. It is NOT a `{StaticResource AppIcon}` XAML attribute: a StaticResource on the root element can only see resources declared before its position in BAML, so it could never resolve the window's own resources and the parse-time failure crashed the shell right after the picker created or opened a project (post-6.6 crash fix, covered by the `MainWindowBootTests` regression).
- **Empty states, monospace paths, keyboard navigation** throughout the export/overhaul surfaces.

## Sprint 6.7 UI hardening fixes

Four manual-testing findings shipped in this pass.

- **Export crash -> detached snackbar presenter.** The first Build click threw `InvalidOperationException: The SnackbarPresenter was never set` because `MainWindow` never attached a `Wpf.Ui.Controls.SnackbarPresenter` to the `Wpf.Ui.ISnackbarService` singleton; `ExportViewModel` snackbar calls crashed the app. Fix: `MainWindow` now hosts `<ui:SnackbarPresenter x:Name="SnackbarPresenter"/>` as the topmost overlay and its constructor injects `Wpf.Ui.ISnackbarService` and calls `SetSnackbarPresenter(SnackbarPresenter)`. Defense in depth: `WpfUiSnackbarService.Show` no-ops when `GetSnackbarPresenter()` is null. `ExportViewBootTests.ExportView_builds_when_navigation_opens_it` (new) resolves the page through the page-provider path on a dedicated STA thread. The boot checks additionally exposed a real latent crash in the same page: `ExportView.xaml` bound `ProgressBar.Value` (TwoWay by default) to the read-only computed `ProgressPercent`, throwing `XamlParseException` the moment the page parsed - fixed with `Mode=OneWay`.
- **Missing close button -> in-content "x".** Both `MainWindow` and `ProjectPickerWindow` now draw a flat white `Dismiss24` "x" button in their top-right corner (keyed `CornerCloseButton` window-local style: transparent template, subtle hover, `Click` -> `Close()`). Declared as a window resource, not app-scope, so the shell still builds headless without an `Application` instance (same trap as the post-6.6 AppIcon fix).
- **No delete option for projects -> per-row Delete.** `ProjectListViewModel.DeleteRecentCommand` (an `AsyncRelayCommand<RecentProjectItem>`, replacing the old selection-only remove) is wired to a per-row Delete button in the picker's recent-list `ItemTemplate`. It confirms, deletes the project folder recursively, forgets the recent entry (and re-runs the drop if the selected entry was deleted); a failed deletion alerts and keeps the entry. Folder stays untouched unless confirmed.
- **Scan button never existed -> automatic .esm scan.** There was no scan UI and every overhaul shipped with a null `Catalog` (dead end: "No catalog - run a scan"). `ProjectViewModel` now injects `FolderCatalogScanner` + `IBackgroundTaskService`; `AddOverhaulAsync` picks the source, scans it automatically on the background task service the moment the esm-bearing folder is chosen (both Vanilla and story-mod), attaches the resulting `Catalog` to the `Overhaul` object initializer and autosaves. A scan failure/cancel surfaces an alert and adds nothing; a scan that yields an empty catalog (e.g. an unreadable plugin) still adds the overhaul with the empty catalog attached.

**Test infrastructure:** the three headless WPF boot tests (`MainWindowBootTests`, `OverhaulViewBootTests`, `ExportViewBootTests`) share one non-parallel `[Collection("WPF Boot Tests")]`, because constructing multiple `FluentWindow`s (or re-resolving `WpfUiThemeService`, whose `ApplicationThemeManager.Apply` re-themes every tracked window) on parallel STA threads races on WPF dispatcher state.

## Post-6.6 crash fix - shell icon resolution

The shell crashed right after the picker created or opened a project with `XamlParseException: Cannot find resource named "AppIcon"` (`MainWindow.xaml` line 7). Root cause: `Icon="{StaticResource AppIcon}"` was set on the MainWindow root element while `AppIcon` lived in the same window's `<FluentWindow.Resources>` - a root-element StaticResource only sees resources declared before its position in BAML, so the window's own later-declared resources were invisible. Fix: the icon moved to app scope (`App.xaml`), `MainWindow.xaml` declares no icon resource and no StaticResource, and `MainWindow.ApplyAppIcon()` resolves it from code. `Application.Current` may be null in headless STA boot checks, hence the null-safe lookup. The regression test `MainWindowBootTests.MainWindow_builds_with_an_open_session` constructs the shell on a dedicated STA thread with an open `IProjectSession` and fails again if a parse-time static icon returns.

## Post-6.6 crash fix - Overhaul screen invalid margin

Clicking "Open" on an overhaul card in the Project screen crashed the app with `XamlParseException: "Auto,0,0,0" is not a valid value for "Margin"` the moment the page was built (v0.6.6.2). Root cause: the "Import patch" button inside the shared popover panel in `OverhaulView.xaml` declared `Margin="Auto,0,0,0"` - `Auto` is a Grid length, not a `Thickness`, so the whole page failed to parse when navigation resolved `OverhaulView` from DI (clicks were the only path that constructed the page; the Sprint 6.5/6.6 interaction suites exercised the view models headlessly, never the page). Fix: the button now uses a plain `Margin="12,0,0,0"`. The regression test `OverhaulViewBootTests.OverhaulView_builds_when_navigation_opens_it` resolves the page through the same `INavigationViewPageProvider.GetPage` path on a dedicated STA thread with an open `IProjectSession` + a selected Overhaul and fails again if an invalid margin returns.

## Sprint 6.8 matrix performance + scan-time filter fixes

Six findings from scanning the real game and opening a large Overhaul catalog landed in this pass.

- **Jewelry and vanilla-enchanted records are filtered at scan time.** Rings and necklaces (`BipedFlags` Amulet/Ring) now skip with `SkipReason.Jewelry = 7`, and items whose name carries a vanilla enchantment suffix (the exact word list, matched longest-first, `OrdinalIgnoreCase`, in the new `VanillaEnchantmentFilter`) skip with `SkipReason.Enchanted = 8`. Both run inside `ArmorSetGrouper.ClassifyGarbage` AFTER `NoArmature`/`EmptyModel`/`NoSlot` and BEFORE `NoKeyword`, so the matrix shows fewer, meaningful armor rows (see `Docs/scanner.md`).
- **Large-matrix RAM/freeze fix.** The old nested `ItemsControl` cell structure materialized thousands of cell cards at once (multi-GB heap, UI freeze on a 3000+ row catalog). The matrix body is now ONE flat `ItemsControl` over the flattened `MatrixItems` list (one `MatrixSectionHeaderViewModel` per section, one `ArmorSetRowViewModel` per row) inside a `ScrollViewer` with `CanContentScroll="True"` + `VirtualizingPanel.IsVirtualizing="True"` + `VirtualizationMode="Recycling"`, so only visible rows are realized. `ArmorSetRowViewModel.DefaultCell` gives the row-name button a meaningful activation target.
- **Scrollbar no longer under the close button.** The body `ScrollViewer` gained `Margin="0,20,0,0"`, dropping the vertical scrollbar below the corner close "x".
- **Long set names display fully.** The `MatrixRowName` style dropped its fixed `Width`/`TextTrimming` for `MaxWidth="220"` + `TextWrapping="Wrap"`, so long names wrap instead of truncating.
- **Clickable row name.** The frozen set-name cell is now a `Button` (`MatrixRow_Click` in `OverhaulView.xaml.cs` + `ActivateCellCommand`/`DefaultCell`), so clicking a row name opens the anchored-popover cell editor just like a mapped column card.

The committed goldens were regenerated (`UW_WRITE_GOLDENS=1`): the mini-universe amulet now reads as jewelry, so `MiniUniverse-catalog.json` dropped the `elven` set (`GroupedSets 11`, `Skipped 3`, `MissingFiles 38`); the static golden `MiniUniverse.esp` is byte-identical.

New headless tests: 4 `ArmorSetGrouperTests` over the new `SyntheticFilteringUniverse` fixture + 2 `OverhaulViewModelTests` (flat `MatrixItems` order, row `DefaultCell` is the first weight column carrying a section variant). Full suite 687 tests green, Release 0 warnings / 0 errors.

## Sprint 6.9 - scan source/filter tuning + set-level replacement editor

Two scanner passes (T1/T1b, see `Docs/scanner.md`) and the replacement-editor reshape (T2) landed in this sprint.

- **Donor-compatibility helpers (`DonorCompatibility`).** `RequiredBodyTypeFor(Gender)` maps the target gender to the body token (male -> HIMBO, else 3BA); `DonorContainsBody(donor, type)` is true when the donor owns BodySlide (`DetectedBodySlideFiles`) or any provided piece mesh path decodes to the required body (`MappingService.BodyMarkerFromPath`); `DonorHasPhysics(donor)` reads `DetectedPhysicsFiles`.
- **Set-level editor data flow.** `ArmorSetDetailViewModel.LoadDonor` resolves the donor piece per target piece via `FindDonorPiece` and runs `MappingService.AssignDonor` once per piece at set level; because a new donor REPLACES the variant's `PieceMapping`s, stale attached patch layers drop out automatically. `LoadBodyPatch`/`LoadPhysicsPatch` attach ONE asset set-level (`AttachPatch`), `ClearBodyPatch`/`ClearPhysicsPatch` detach. `UnloadDonorIfUnreferenced` unloads a replaced donor from the library only when no mapping in ANY overhaul references it (main donor or attached patch layer) - the user-confirmed accounting ruling; other sets are never touched.
- **Tests.** `ArmorSetDetailViewModelTests` rewritten from 9 to 14 headless tests over a single-library fixture (`project.Library` is the shared donor list for the editor, `MappingService` and the assertions): empty Load-Armor state, donor flags -> checkmarks vs patch rows, female->3BA vs male->HIMBO requirement (a separate male set fixture), change-donor unload-unreferenced vs keep-while-referenced, set-level assign + body/physics patch fan-out with correct `GetArmorSetStatus`/`GetOverhaulProgress`, autosave flush + close flush, per-op status refresh, import-patch navigation. Full suite 697 tests green (692 + 6), Release 0 warnings / 0 errors, no artifacts.

## Sprint 6.10 - user-reported fixes: recent-open DB recreation, editor Load Armor picker

Three real-usage findings landed in this pass; the scan-side DLC enchantment fix is documented in `Docs/scanner.md`.

- **Recent projects reopen even without `project.db` (`ProjectListViewModel`).** `OpenSelectedRecentCommand` used to pass the recent item's stored DB path as the folder root, and `OpenRootAsync` appends `project.db` itself - so a click produced the bogus `<Proj>\project.db\project.db` and EVERY recent open failed. The command now derives the project folder with `Path.GetDirectoryName` and opens via `OpenRootAsync(folder, createIfMissing: true)`: a lost/corrupt database is recreated and the session opens. The manual "Open project" folder dialog still alerts when `project.db` is absent (that test is unchanged). New headless tests `OpenRecent_existing_db_resolves_session_from_folder` and `OpenRecent_missing_db_recreates_db_and_opens`.
- **"Load Armor" works with no donor selected (`ArmorSetDetailViewModel`).** A bare "Load Armor" click used to do nothing because the synchronous command only acted on the ComboBox selection. `LoadDonorCommand` is now an `IAsyncRelayCommand` over `LoadDonorOrImportAsync`; with a donor selected it assigns it, otherwise it opens a mod-archive picker (`IAppDialogService.PickModArchiveAsync`, WPF `Microsoft.Win32.OpenFileDialog`, filter "Mod archives (*.7z;*.zip;*.rar)", Null stub returns null), runs the existing `IDonorImportRunner` (a new optional ctor dep of the editor, threaded through `OverhaulViewModel`), and assigns the archive's `FullReplacer` donor in ONE step. An imported asset that is not a compatible `FullReplacer` alerts but stays in the donor library. New headless tests: selection assigns, picker imports + assigns (a custom runner `OnImport` yields a compatible Female/Heavy FullReplacer), cancelled picker no-ops.

Full suite 705 tests green (697 + 8: 3 `ArmorSetGrouper` + 2 `ProjectListViewModel` + 3 `ArmorSetDetailViewModel`), Release 0 warnings / 0 errors, no artifacts.

## Optimization Phase A - Immediate deblocking: debounce, offload, cancel (done)

Typing in the Overhaul matrix search box froze the UI for 30-60 seconds per keystroke on a 651-set vanilla catalog and far longer on 3000+ row story-mod catalogs. Root causes: `SearchText` with `UpdateSourceTrigger=PropertyChanged` triggered a synchronous `OverhaulMatrix.Build` on the UI thread per character, `OverhaulView.xaml` wrapped `ItemsControl` in an explicit `ScrollViewer` breaking virtualization, and `BuildCardLines`/`GetArmorSetStatus` used linear `FirstOrDefault` scans per cell.

Phase A (no behavioral change) lands the three immediate fixes from `Plans/optimization.md`:

- **A1 - Debounce `SearchText` binding.** `OverhaulView.xaml:78` now uses `Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged, Delay=250}"`. WPF coalesces rapid keystrokes in the binding engine, preserving current semantics while throttling bound updates to one per 250 ms.

- **A2 - Async, cancellable filtering off the UI thread.** `OverhaulViewModel` now takes an optional `IBackgroundTaskService` (DI-injected, headless tests pass `null` and keep the original synchronous path). `SearchText`/`StatusFilter` setters call `RequestFilter()` which cancels the previous `CancellationTokenSource`, increments a generation counter, snapshots `Catalog`/`mappings`/`DonorLibrary` on the UI thread, and runs `OverhaulMatrix.Build` on `IBackgroundTaskService.RunAsync("Filter matrix", ...)` . Stale generations are dropped, cancellations are swallowed, and the `OverhaulMatrixViewModel` (pure POCO `MatrixCellViewModel` etc.) is the only object constructed off-thread - no `DispatcherObject`/`Brush` is created off-thread (thread-affinity guard). `OnCellEdited` also uses the async preserve-editor path when the service is available, otherwise the original synchronous `RecomputeMatrixPreserveEditor`.

- **A3 - Cached `ProgressLabel`.** `ProgressLabel` changed from a computed getter (`GetOverhaulProgress` per access, doubling the per-filter work) to a stored property updated alongside `Columns`/`Sections`/`MatrixItems` in `RecomputeMatrix`/`FilterAsync`. `RaiseMatrixChanged` only raises `OnPropertyChanged`, it no longer recomputes.

Headless `OverhaulViewModelTests` (13) still use the synchronous path and remain green; real app uses the async path. Full suite 705 tests green, Release 0 warnings / 0 errors, no artifacts.

## Optimization Phase B - Fix UI virtualization - required for 3000+ row catalogs (done)

The Sprint 6.8 flat `ItemsControl` inside an explicit `ScrollViewer` defeated `VirtualizingStackPanel` - an outer `ScrollViewer` gives infinite available height, so all 6000+ rows were realized.

- **B1 - Replace `ScrollViewer` + `ItemsControl` with a virtualizing `ListView`.** `OverhaulView.xaml:113-188` now hosts a `ListView` that owns its internal `ScrollViewer`. Properties: `Margin="0,20,0,0"`, `Visibility` bound to `IsEmpty` inverse, `Background="Transparent"`, `BorderThickness="0"`, `ScrollViewer.CanContentScroll="True"`, `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`, `VirtualizingPanel.IsVirtualizing="True"`, `VirtualizingPanel.VirtualizationMode="Recycling"`, `VirtualizingPanel.ScrollUnit="Pixel"` (smooth pixel scrolling while still virtualizing), explicit `VirtualizingStackPanel` in `ItemsPanel`, `SelectionMode="Single"`. The existing flat `MatrixItems` list and `DataTemplate`s for `MatrixSectionHeaderViewModel` vs `ArmorSetRowViewModel` (with `MatrixRow_Click`/`MatrixCell_Click` `Button`s) are preserved inside `ListView.Resources`. Selection chrome guard: `ItemContainerStyle` sets transparent `Background`/`BorderThickness`/`Padding`, `FocusVisualStyle={x:Null}`, and triggers on `IsSelected`/`IsMouseOver` keep background transparent, so custom row templates keep their visuals and `ActivateCellCommand` still routes via `RelativeSource AncestorType=Page`. Keyboard navigation via `ListView` selection is intentionally suppressed.

- **B2 - Keep collection reference stability.** `OverhaulViewModel.cs` added `AreColumnsEqual` (compares `Weight` per column), `ApplyMatrixResult` and `RaiseMatrixChangedWithoutColumns`. `RecomputeMatrix`, `FilterAsync`, `FilterPreserveEditorAsync` and `RecomputeMatrixPreserveEditor` now keep the existing `Columns` reference when `AreColumnsEqual(Columns, matrix.Columns)` and raise only `Sections`/`MatrixItems`/`ProgressLabel` etc. without `Columns`. Filtering by `SearchText`/`StatusFilter` therefore does not recreate the weight-column header control when the catalog has not changed (catalog-dependent columns only, search-independent - Phase C2 minimal).

- **B3 - Row virtualization tuning.** Same `ListView` settings (`CanContentScroll`, `IsVirtualizing`, `Recycling`, `ScrollUnit Pixel`) verified via headless `OverhaulViewBootTests` that the view builds with an open session + selected overhaul and that realized container count stays ~2x viewport size.

Full suite 705 tests green (including 4 boot tests), Release 0 warnings / 0 errors, no artifacts.

## Optimization Phase C - Algorithmic rewrite of `OverhaulMatrix.Build` - required for instant filter (done)

Per-keystroke filtering was `O(S * P * M + C * D)` due to linear scans per cell.

- **C1 - Index donor library.** `OverhaulMatrix.Build` now builds `Dictionary<Guid, DonorAsset> donorById` from `DonorLibrary.Assets` once per `Build` - `O(D)` - and `BuildCardLinesFast` uses `TryGetValue` instead of `FirstOrDefault` per distinct donor/patch per cell.

- **C2 - Cache columns and set metadata once per catalog.** `ConditionalWeakTable<Catalog, CachedCatalogData>` caches `Columns` and per-set `SetMeta` (`DisplayNameLower`, `BelongsFemale`/`BelongsMale`, `VariantBySectionWeight` dict) built once per `Catalog` reference. `SetBelongsToSection` and `VariantFor` become `O(1)` dictionary lookups; search reuses `DisplayNameLower`. The cache is reference-keyed and thread-safe, and `Build` reuses it across filter invocations.

- **C3 - Index mappings.** `Build` builds `Dictionary<(SetId, PieceId, Gender), PieceMapping> mappingsByKey` and `Dictionary<string, List<PieceMapping>> mappingsBySet` once per `Build` - `O(M)` - and `GetMappingsForVariantFast`/`BuildCellFast` use `TryGetValue` + Unisex fallback scan over the small per-set list instead of scanning all mappings per piece. `PiecesMappingsFor` per cell drops from `O(P * M)` to `O(P)`.

- **C4 - Cache armor-set status per mappings snapshot.** `Build` computes `Dictionary<string, ArmorSetStatus> statusBySetId` in a single pass `O(S * P)` via `ComputeStatusFast` (exact gender match, mirroring `MappingService.GetArmorSetStatus`), avoiding `S` calls to `GetArmorSetStatus` which each scanned `mappings`. `MappingService.GetOverhaulProgress` also optimized to build `ToLookup`/`byKey` once and compute statuses via `GetArmorSetStatusFast` instead of `S` scans - same `O(M + totalPieces)` cost. Do not cache `statusBySetId` across `Build` invocations on bare `ReferenceEquals(mappings, _cachedMappings)` - see C4a.

- **C4a - Fix cache invalidation for in-place `Overhaul.Mappings` mutation - chosen: recompute per `Build` (Option 2 style, no persistent status cache).** Hazard: `Overhaul.Mappings` is a mutable `List<PieceMapping>` mutated in place (`RemoveAll`/`Add`/`ReplaceInList`). A persistent `statusBySetId` cache keyed by list reference would return stale status after a popover edit `AssignDonor`/`AttachPatch` etc. Decision: do not persist `statusBySetId` across `Build` calls; recompute it per `Build` from the `mappingsSnapshot` (`ToList()` copy taken on the UI thread in `OverhaulViewModel.ScheduleFilterAsync`). `ConditionalWeakTable` is only for catalog-level immutable data. This preserves the tested contract "status refresh after each op" and is documented as the invalidation choice; the E2 regression test must warm the catalog cache then mutate `Mappings` in place via `AssignDonor` and verify the next `Build` reflects the new status (not stale).

- **C5 - Optimize search predicate.** Normalize `search` once via `ToLowerInvariant()` and store `DisplayNameLower` per set in `SetMeta`; per-set check is `DisplayNameLower.Contains(searchLower, Ordinal)` instead of per-set `OrdinalIgnoreCase`.

- **C6 - Avoid per-filter allocation of lines for blank cells.** `BuildCellFast` returns `Blank` without calling `BuildCardLinesFast` when `variant is null` or `setMappings.Count == 0`, reusing the existing `Blank` singleton path.

Net per-filter work drops from `O(S * P * M + C * D)` to `O(D + M + S + C)` with hash lookups; on vanilla `S=651, C~3900` this is sub-millisecond for the search branch plus status indexing once per catalog.

Full suite 705 tests green, Release 0 warnings / 0 errors, no artifacts.

## Optimization Phase D - Collection-view and incremental filtering polish (done)

**D1 - Keep `Catalog.Sets` as `CollectionView` or filtered projection - evaluated, kept `Build`.** `ICollectionView` over `MatrixItems` with predicate `DisplayNameLower.Contains(searchLower)` would still iterate `S` and would require keeping `MatrixItems` stable across filters, complicating invalidation when `Overhaul.Mappings` change (status changes require rebuild). Current `Build` with search prefilter already avoids rebuilding cells for filtered-out sets and per-filter cost after Phase C is `O(D + M + S_passing * P)` with hash lookups (<5 ms for 651 sets), so `Build` is kept. Decision recorded in `OverhaulMatrix.cs` comment.

**D2 - Enable text-search prefilter before heavy cell building.** `OverhaulMatrix.Build` now orders `BelongsToSection` -> `searchLower` check (first filter, `O(1)` via `DisplayNameLower`) -> lazy `ComputeStatusFast` via `statusCache` (on-demand, cached per `Build` for Unisex duplicate rows) -> `BuildCellFast`. Pre-Phase C, `statusBySetId` was precomputed for all `S` sets; now status is computed only for passing sets (e.g., "iron" matches 1 of 651, only 1 status computed).

Full suite 705 tests green, Release 0 warnings / 0 errors, no artifacts.

## Optimization Phase E - Testing and measurement (done)

**E1 - Benchmark harness.** `tests/UltimateWardrobe.Tests/App/OverhaulMatrixBenchmarkTests.cs` with `Stopwatch` headless tests for `OverhaulMatrix.Build`: `Build_651_sets_completes_under_50ms` and `Build_3000_sets_completes_under_150ms` warm the `ConditionalWeakTable` catalog cache then measure single `Build`; `Build_with_search_iron_on_651_sets_is_submillisecond_after_cache` verifies filtered path. Thresholds assert indexed `Build` stays <50 ms for vanilla 651 sets and <150 ms for 3000 sets.

**E2 - Regression tests for filtering - covers cached path with in-place mutation.** `tests/UltimateWardrobe.Tests/App/OverhaulFilteringRegressionTests.cs` (7 tests):
- `Debounced_async_filter_cancels_previous_and_applies_last_search` - `OverhaulViewModel` with `DispatcherBackgroundTaskService`, rapid `SearchText="a"` then `"iron"`, poll up to 2 s for async `Sections` to settle to `Iron Armor` only.
- `Search_preserves_Columns_reference_when_catalog_unchanged` (sync) and `with_background_service` (async) - `Columns` `BeSameAs` before after `SearchText="iron"`.
- `MatrixItems_order_still_FEMALE_header_then_rows_then_MALE_header_after_indexed_rewrite` - top 10 items order.
- `Donor_index_path_produces_same_cell_lines_as_expected_golden` - `CellAt(0,0,0)` lines `Set/Donor/BodyPatch/PhysicsPatch` golden.
- `Cached_status_regression_in_place_mutation_reflects_new_mapping_same_list_reference` and `In_place_mutation_same_list_reference_must_not_return_stale_status_via_ReferenceEquals` - warm `NotStarted`, capture `mappingsRef`, `MappingService.AssignDonor` in place on same `Overhaul` instance (`ReferenceEquals` stays true), `vm.Refresh()` must show `Mapped` and non-blank cell with donor line and `ProgressLabel` `1 mapped` - would fail on bare `ReferenceEquals` cache.

**E3 - UI smoke.** `tests/UltimateWardrobe.Tests/App/OverhaulViewVirtualizationTests.cs` - `OverhaulView_ListView_is_virtualizing_and_large_catalog_opens` builds 3000-set synthetic catalog, opens `OverhaulView` via `INavigationViewPageProvider` on STA thread in a hidden `Window`, finds descendant `ListView`, asserts `VirtualizingPanel.GetIsVirtualizing==true`, `GetVirtualizationMode==Recycling`, `ScrollViewer.GetCanContentScroll==true`, `GetScrollUnit==Pixel`, and that `MatrixItems.Count>3000` with `Columns>0` and total time <2 s.

Full suite 716 tests green (705 + 11 new: 3 benchmark + 7 regression + 1 virtualization), Release 0 warnings / 0 errors, no artifacts.

## Screens still to come

None for Phase 6. Phase 7 targets story-mod (non-Vanilla) overhauls and Postbone/post-build facilities.
