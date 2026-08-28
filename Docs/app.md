# App - WPF Shell

> Phase 6 (Sprint 6.1 done) - `src/UltimateWardrobe.App` is the WPF desktop shell. `net10.0-windows`, `UseWPF=true`, CommunityToolkit.Mvvm ViewModels, WPF-UI 4.3.0 Fluent shell, Microsoft.Extensions.Hosting composition root, Serilog logging. This doc records the composition/host lifecycle, the startup gate, the WPF-UI 4.3.0 service wiring and the Sprint 6.1 spike conclusions.

## Stack

- WPF-UI **4.3.0** (packages under `lib/net10.0-windows7.0/` - valid on `net10.0-windows`)
- CommunityToolkit.Mvvm 8.4.0, Microsoft.Extensions.Hosting 10.0.0, Microsoft.Extensions.Logging.Abstractions
- Serilog 4.2.0 + Serilog.Sinks.File 6.0.0 + Serilog.Extensions.Hosting 8.0.0

`UltimateWardrobe.App` is the only project referencing the UI/hosting packages; every `src/*` sibling stays frozen. The output is `WinExe` with `app.manifest` (PerMonitorV2 DPI). `Version` is `0.6.1`.

## Composition root - `CompositionRoot.Register(IServiceCollection services, bool registerUi = true)`

The single composition root is shared by the real app and the headless tests. `registerUi=true` adds the WPF-UI-backed adapters and Views; `registerUi=false` (headless, amendment 2) registers no-op stubs at the adapter interfaces so every ViewModel and app service resolves with no WPF control ever constructed.

Always registered:
- App services: `ILogViewer`/`LogViewer`, `RecentProjectsStore`, `IProjectSession`/`ProjectSession`, `IProjectStoreFactory`/`ProjectStoreFactory`, `IOverhaulSourceValidator`/`OverhaulSourceValidator`, `IBackgroundTaskService`/`DispatcherBackgroundTaskService`.
- Domain services: `CompositeExtractor` / `IArchiveExtractor`, `FolderCatalogScanner`, `DonorClassifier`, `DonorImportService`, `DonorLibraryService`, `IPatcher`/`WardrobePatcher`, and `MappingService` (a factory that requires an open `IProjectSession` and constructs `new MappingService(session.Project.Library)`).
- ViewModels: `ProjectListViewModel`, `MainViewModel`, `ProjectViewModel`, `OverhaulViewModel`, `ArmorSetDetailViewModel`, `DonorLibraryViewModel`, `ExportViewModel`.

`registerUi=true` additionally registers the WPF-UI wiring (below) and the Views (`MainWindow`, `ProjectPickerWindow`, `ProjectView`, `OverhaulView`, `ExportView`). `registerUi=false` additionally registers `NullAppNavigationService`, `NullAppDialogService`, `NullSnackbarService`.

A guarded marker type makes a second `Register` call on the same `IServiceCollection` throw (`CompositionRoot.Register has already been called for this service collection.`).

## Host lifecycle - `App.OnStartup` / `App.OnExit`

`App` (a `System.Windows.Application`) builds a `Host.CreateApplicationBuilder()` on the UI thread in `OnStartup`, configures Serilog (file sink into `%LocalAppData%\UltimateWardrobe\logs\app-{Date}.log`, rolling day, retained 7, plus a `LogViewerSink` forwarding every rendered line into `ILogViewer`), calls `CompositionRoot.Register`, builds and starts the host, then runs the startup gate. `ShutdownMode` is `OnExplicitShutdown`. `_host.Dispose()` runs on `OnExit`.

## Startup gate - single project per process (amendment 7)

`App.OnStartup` resolves `ProjectPickerWindow` (hosting `ProjectListViewModel`) and shows it modally BEFORE the shell. A canceled/empty picker leaves `IProjectSession` closed and the app calls `Shutdown(0)` with no shell. On a successful pick/create `ProjectListViewModel.OpenRootAsync` opens the `project.db` via `IProjectStoreFactory`, publishes the single open project on `IProjectSession`, records it in `RecentProjectsStore`, and raises `CloseRequested` so the picker closes. `App` then resolves and shows `MainWindow` scoped to that one project for the process lifetime. There is no in-app "switch/close project" command - relaunch to change projects. `ProjectListViewModel` stays headless-testable (hosted by the picker, not a navigation page).

## WPF-UI 4.3.0 service wiring (the Sprint 6.1 spike result)

The exact wiring is resolved against the shipped 4.3.0 API and verified by a headless STA boot check (FluentWindow shown, Loaded fires, page provider attached, programmatic navigation to `ProjectView` succeeds, no exception):

1. `Wpf.Ui.Abstractions.INavigationViewPageProvider` - WPF-UI 4.x RENAMED the planner's "IPageService" to this abstraction. Own implementation `AppNavigationViewPageProvider` resolves page instances from the composition root: `object GetPage(Type pageType) => _services.GetRequiredService(pageType)`.
2. `Wpf.Ui.NavigationService` is registered in DI over that page provider and resolved as `Wpf.Ui.INavigationService`: `new Wpf.Ui.NavigationService(pageProvider)` (its ctor takes the `INavigationViewPageProvider`).
3. In `MainWindow.OnLoaded` (surface method `InitializeNavigation`): `RootNavigation.SetPageProviderService(_pageProvider)` attaches the DI page provider to the `NavigationView` so item clicks resolve pages from DI, and `_navigationService.SetNavigationControl(RootNavigation)` binds the programmatic service to the same control. `RootNavigation.Navigate(typeof(ProjectView))` lands the first screen. The window's class doc for the shell navigation: `NavigationView` items (`NavigationViewItem`) declare `TargetPageType` and navigate through the page provider.
4. Dialogs fall back to `Microsoft.Win32.OpenFolderDialog` for folder picking - WPF-UI 4.3 ships no folder picker (spike conclusion). Forms/closes use the WPF-UI `ContentDialog` host when attached, else `MessageBox`.

## Spike conclusions (recorded for maintainers)

- **Symbol set**: `Wpf.Ui.Controls.SymbolRegular` has NO plain `Cancel` / `Stop` / `Dismiss` / `ChromeClose` member. Using `Cancel24` throws at XAML parse (`XamlParseException: Cancel24 is not a valid value for SymbolRegular`). Valid members used: `Home24`, `GridDots24`, `Save24`, `Add24`, `FolderOpen24`. Avoid non-existent symbols - they fail at runtime, not compile.
- **No `ui:Page` control in 4.3**: WPF-UI removed/renamed the `Page` control (compiler `MC3074: tag "Page" does not exist`). Placeholder pages inherit standard `System.Windows.Controls.Page`; do NOT use `<ui:Page>`.
- **Themes**: theme dictionaries live in the `Wpf.Ui.Markup` namespace - `ThemesDictionary` (`Theme="Dark"`, dark default) and `ControlsDictionary`, merged in `App.xaml`. WPF `Page` has no `Padding` property (use a margin on the content).
- **`System.IO` is NOT an implicit using for `UseWPF=true` SDK projects** (WPF implicit usings omit `System.IO`); add `using System.IO;` explicitly wherever `Path`/`File`/`Directory`/`IOException` are used.
- **`NavigationView.Navigate` requires the applied window template** (content presenter). In the real app `OnLoaded` handles this; in a headless test you must `Show()` + pump the dispatcher (or `ApplyTemplate` + layout) or it NREs inside `UpdateContent`.
- **MessageBox ambiguity**: with both `using System.Windows;` and `using Wpf.Ui.Controls;`, `MessageBox`/`MessageBoxButton`/`MessageBoxResult` are ambiguous - alias them to `System.Windows.*`.
- **`NoWarn` scoping**: NU package-version warnings (`NU1900..NU1904`) are throttled in `UltimateWardrobe.App.csproj` only (`NoWarn` + `WarningsNotAsErrors`). The zero-warnings Release gate still holds for the whole solution. The App project also emits XAML/template warnings that are absorbed by the build.

## Screen / navigation map (roadmap 8.2, minus a shell project list)

```
App.OnStartup
  ProjectPickerWindow  - recent projects, "New project", "Open project" (ProjectListViewModel)

MainWindow (FluentWindow)
  NavigationView pane
    Project       - placeholder (Sprint 6.2/6.3: overhaul cards + donor library)
    Overhaul      - placeholder (Sprint 6.4/6.5: mapping matrix grid + popover)
    Export        - placeholder (Sprint 6.6: checklist + build + result)
  Status bar       busy spinner (IsBusy), latest ILogViewer line, Cancel, version in title
```

The status bar is bound to `MainViewModel` (busy spinner visibility to `IsBusy`, live text to `StatusText` fed by `ILogViewer.LineAppended`, `CancelCommand`, version in `Title`). The `StatusBar`/version title and the picker's recent list are placeholders in this sprint; the deep screens arrive in Sprints 6.2-6.6.

## Settings layout

Repository-owned data lives under the user's Project root (`project.db`, `Source/`, `CatalogCache/`, `Export/`, `logs/`). App-level settings (recent projects, last theme) live under `%LocalAppData%\UltimateWardrobe\` - `RecentProjectsStore` reads/writes `settings.json` there (top-8 recent `project.db` paths, newest first, dedup, corrupt file -> empty).

## MVVM conventions

`ObservableObject` partial classes with `[ObservableProperty]` / `[RelayCommand]` / `[AsyncRelayCommand]`, `ObservableCollection<T>` for lists, `AsyncRelayCommand` + `CanExecute` as the "busy" guard. ViewModels depend only on App-layer interfaces (`IAppNavigationService`, `IAppDialogService`, `ISnackbarService`, `IBackgroundTaskService`, `IOverhaulSourceValidator`, `ILogViewer`) so they run headless in xUnit; the WPF-UI-backed implementations live in `Views/Infrastructure`.

## Screens still to come

- Sprint 6.2 - `ProjectListViewModel` full picker flow (recent/new/open), `ProjectViewModel` overhaul cards + add/rename/delete, persistence wiring/autosave.
- Sprint 6.3 - `DonorLibraryViewModel` table + drag-and-drop import drop zone.
- Sprint 6.4 - `OverhaulViewModel` mapping matrix grid (FEMALE/MALE ARMOR section rows x weight columns, 2-D virtualization).
- Sprint 6.5 - `ArmorSetDetailViewModel` anchored-popover cell editor.
- Sprint 6.6 - `ExportViewModel` checklist + `IPatcher` invocation + result card + polish + manual E2E.
