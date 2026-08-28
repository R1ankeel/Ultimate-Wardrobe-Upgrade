using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.ObjectModel;
using System.IO;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.Views;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Mapping;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// One Overhaul card on the Project screen (Phase 6 Sprint 6.2): the immutable progress snapshot
/// shown on the card plus a reference to the underlying <see cref="Core.Domain.Overhaul"/> so the
/// per-card rename/delete/select commands can act on it.
/// </summary>
public sealed record OverhaulCardViewModel(
    Overhaul Overhaul,
    int TotalSets,
    int MappedCount,
    int NeedsPatchCount,
    int RemainingCount,
    double DoneFraction,
    string StatusLabel)
{
    public string Name => Overhaul.Name;

    public static OverhaulCardViewModel From(Overhaul overhaul, OverhaulProgress progress)
    {
        var total = progress.TotalSets;
        string status;
        if (total == 0)
        {
            status = "No catalog - run a scan";
        }
        else if (progress.Done == total)
        {
            status = "Complete";
        }
        else if (progress.Done + progress.Mapped > 0)
        {
            status = "In progress";
        }
        else
        {
            status = "Not started";
        }

        return new OverhaulCardViewModel(
            overhaul,
            total,
            progress.Mapped + progress.Done,
            progress.NeedsPatch,
            progress.Remaining,
            progress.DoneFraction,
            status);
    }
}

/// <summary>
/// Project screen (Phase 6 Sprint 6.2): the overhaul cards - name, DoneFraction, mapped/total and
/// status - plus Add (Vanilla / StoryMod) through the folder picker + <see cref="IOverhaulSourceValidator"/>,
/// and per-card Rename / Delete / Select (navigate to the Overhaul matrix screen). Every mutation
/// mutates the <see cref="IProjectSession"/> project and flushes through its shared
/// <see cref="Core.Abstractions.IProjectStore"/> (amendment 3 autosave). The ViewModel is headless:
/// only App-layer abstractions are injected.
/// </summary>
public sealed class ProjectViewModel : ObservableObject
{
    private readonly IProjectSession _session;
    private readonly IAppNavigationService _navigation;
    private readonly IAppDialogService _dialogs;
    private readonly IOverhaulSourceValidator _validator;
    private readonly IOverhaulSelection _overhaulSelection;
    private readonly MappingService _mapping;
    private readonly FolderCatalogScanner _scanner;
    private readonly IBackgroundTaskService _backgroundTasks;
    private readonly ILogger<ProjectViewModel> _logger;
    private bool _isBusy;
    private OverhaulCardViewModel? _selectedOverhaul;
    private IAsyncRelayCommand? _addVanillaCommand;
    private IAsyncRelayCommand? _addStoryModCommand;
    private IAsyncRelayCommand<OverhaulCardViewModel>? _renameCommand;
    private IAsyncRelayCommand<OverhaulCardViewModel>? _deleteCommand;
    private IRelayCommand<OverhaulCardViewModel>? _selectCommand;

    public ProjectViewModel(
        IProjectSession session,
        IAppNavigationService navigation,
        IAppDialogService dialogs,
        IOverhaulSourceValidator validator,
        IOverhaulSelection overhaulSelection,
        MappingService mapping,
        FolderCatalogScanner scanner,
        IBackgroundTaskService backgroundTasks,
        ILogger<ProjectViewModel>? logger = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _overhaulSelection = overhaulSelection ?? throw new ArgumentNullException(nameof(overhaulSelection));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));
        _logger = logger ?? NullLogger<ProjectViewModel>.Instance;
    }

    public string ProjectName => _session.Project?.Name ?? string.Empty;

    public string ProjectRoot => _session.Project?.RootPath ?? string.Empty;

    public bool IsOpen => _session.IsOpen;

    public ObservableCollection<OverhaulCardViewModel> Overhauls { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public OverhaulCardViewModel? SelectedOverhaul
    {
        get => _selectedOverhaul;
        set => SetProperty(ref _selectedOverhaul, value);
    }

    public IAsyncRelayCommand AddVanillaOverhaulCommand =>
        _addVanillaCommand ??= new AsyncRelayCommand(
            () => AddOverhaulAsync(SourceKind.Vanilla),
            () => IsOpen && !IsBusy);

    public IAsyncRelayCommand AddStoryModOverhaulCommand =>
        _addStoryModCommand ??= new AsyncRelayCommand(
            () => AddOverhaulAsync(SourceKind.StoryMod),
            () => IsOpen && !IsBusy);

    public IAsyncRelayCommand<OverhaulCardViewModel> RenameOverhaulCommand =>
        _renameCommand ??= new AsyncRelayCommand<OverhaulCardViewModel>(
            RenameOverhaulAsync,
            card => IsOpen && !IsBusy && card is not null);

    public IAsyncRelayCommand<OverhaulCardViewModel> DeleteOverhaulCommand =>
        _deleteCommand ??= new AsyncRelayCommand<OverhaulCardViewModel>(
            DeleteOverhaulAsync,
            card => IsOpen && !IsBusy && card is not null);

    public IRelayCommand<OverhaulCardViewModel> SelectOverhaulCommand =>
        _selectCommand ??= new RelayCommand<OverhaulCardViewModel>(
            SelectOverhaul,
            card => IsOpen && card is not null);

    /// <summary>Rebuild the card list from the <see cref="IProjectSession"/> project graph.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectRoot));
        OnPropertyChanged(nameof(IsOpen));
        NotifyCanExecuteChanged();

        Overhauls.Clear();
        var project = _session.Project;
        if (project is null)
        {
            return;
        }

        foreach (var overhaul in project.Overhauls)
        {
            var progress = ComputeProgress(overhaul);
            Overhauls.Add(OverhaulCardViewModel.From(overhaul, progress));
        }
    }

    private OverhaulProgress ComputeProgress(Overhaul overhaul)
    {
        if (overhaul.Catalog is null || overhaul.Catalog.Sets.Count == 0)
        {
            return new OverhaulProgress();
        }

        return _mapping.GetOverhaulProgress(overhaul.Mappings, overhaul.Catalog);
    }

    private async Task AddOverhaulAsync(SourceKind kind)
    {
        if (IsBusy || !IsOpen)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var source = await PickSourceAsync(kind);
            if (source is null)
            {
                return;
            }

            var catalog = await ScanSourceAsync(source);
            if (catalog is null)
            {
                return;
            }

            var project = _session.Project!;
            var name = SourceDefaultName(source);
            var overhaul = new Overhaul(Guid.NewGuid(), name, project.Id, source) { Catalog = catalog };
            project.Overhauls.Add(overhaul);

            await SaveAsync();
            Refresh();
            _logger.LogInformation(
                "Added {Kind} overhaul '{Name}' with {SetCount} scanned armor sets.",
                kind,
                name,
                catalog.Sets.Count);
        }
        finally
        {
            IsBusy = false;
            NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Runs the folder scan for a freshly picked source on the background task service (Sprint 6.7:
    /// the scan starts automatically as soon as the esm-bearing folder is chosen - there is no scan
    /// button) and returns the catalog. A failed/cancelled scan surfaces an alert and yields null, so
    /// the caller does not add an unusable overhaul.
    /// </summary>
    private async Task<Catalog?> ScanSourceAsync(CatalogSource source)
    {
        Catalog? catalog = null;
        try
        {
            await _backgroundTasks.RunAsync("Scanning the overhaul source", async ct =>
            {
                catalog = await _scanner.ScanAsync(source, ct);
            });
            return catalog;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Scan cancelled for '{Root}'.", source.RootPath);
            return null;
        }
        catch (CatalogScanException ex)
        {
            _logger.LogError(ex, "Scan failed for '{Root}'.", source.RootPath);
            await _dialogs.AlertAsync("Scan failed", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed for '{Root}'.", source.RootPath);
            await _dialogs.AlertAsync("Scan failed", $"The source could not be scanned: {ex.Message}");
            return null;
        }
    }

    private async Task<CatalogSource?> PickSourceAsync(SourceKind kind)
    {
        if (kind == SourceKind.Vanilla)
        {
            var gameRoot = await _dialogs.PickFolderAsync(
                "Add vanilla overhaul - choose the game root",
                string.Empty);
            if (string.IsNullOrWhiteSpace(gameRoot))
            {
                return null;
            }

            var errors = _validator.ValidateVanilla(gameRoot);
            if (errors.Count > 0)
            {
                await _dialogs.AlertAsync("Invalid source", string.Join(Environment.NewLine, errors));
                return null;
            }

            return new VanillaCatalogSource(gameRoot);
        }

        var storyGameRoot = await _dialogs.PickFolderAsync(
            "Add story mod overhaul - choose the game root",
            string.Empty);
        if (string.IsNullOrWhiteSpace(storyGameRoot))
        {
            return null;
        }

        var modRoot = await _dialogs.PickFolderAsync(
            "Add story mod overhaul - choose the mod root",
            string.Empty);
        if (string.IsNullOrWhiteSpace(modRoot))
        {
            return null;
        }

        var mainPlugin = await _dialogs.PromptTextAsync(
            "Add story mod overhaul",
            "Enter the main plugin file name (for example Vigilant.esm):",
            "Vigilant.esm");
        if (string.IsNullOrWhiteSpace(mainPlugin))
        {
            return null;
        }

        var storyErrors = _validator.ValidateStoryMod(storyGameRoot, mainPlugin, modRoot);
        if (storyErrors.Count > 0)
        {
            await _dialogs.AlertAsync("Invalid source", string.Join(Environment.NewLine, storyErrors));
            return null;
        }

        return new StoryModCatalogSource(modRoot, mainPlugin);
    }

    private static string SourceDefaultName(CatalogSource source)
    {
        var root = source.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(root);
    }

    private async Task RenameOverhaulAsync(OverhaulCardViewModel? card)
    {
        if (card is null || IsBusy || !IsOpen)
        {
            return;
        }

        var newName = await _dialogs.PromptTextAsync(
            "Rename overhaul",
            "Enter the new name:",
            card.Overhaul.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == card.Overhaul.Name)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var project = _session.Project!;
            var index = project.Overhauls.IndexOf(card.Overhaul);
            if (index < 0)
            {
                return;
            }

            project.Overhauls[index] = Renamed(card.Overhaul, newName);
            await SaveAsync();
            Refresh();
            _logger.LogInformation("Renamed overhaul to '{Name}'.", newName);
        }
        finally
        {
            IsBusy = false;
            NotifyCanExecuteChanged();
        }
    }

    private async Task DeleteOverhaulAsync(OverhaulCardViewModel? card)
    {
        if (card is null || IsBusy || !IsOpen)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Delete overhaul",
            $"Delete '{card.Overhaul.Name}'? This removes the overhaul and its mappings.");
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var project = _session.Project!;
            project.Overhauls.Remove(card.Overhaul);
            await SaveAsync();
            Refresh();
            _logger.LogInformation("Deleted overhaul '{Name}'.", card.Overhaul.Name);
        }
        finally
        {
            IsBusy = false;
            NotifyCanExecuteChanged();
        }
    }

    private void SelectOverhaul(OverhaulCardViewModel? card)
    {
        if (card is null || !IsOpen)
        {
            return;
        }

        _logger.LogInformation("Opening overhaul screen for '{Name}'.", card.Overhaul.Name);
        _overhaulSelection.Select(card.Overhaul.Id);
        _navigation.Navigate(typeof(OverhaulView));
    }

    private async Task SaveAsync()
    {
        var store = _session.Store;
        if (store is null || _session.Project is null)
        {
            return;
        }

        await store.SaveAsync(_session.Project);
    }

    private static Overhaul Renamed(Overhaul source, string newName)
    {
        var renamed = new Overhaul(source.Id, newName, source.ProjectId, source.Source)
        {
            Policy = source.Policy,
            Catalog = source.Catalog,
            CreatedAt = source.CreatedAt,
            ModifiedAt = DateTime.UtcNow,
        };
        renamed.Mappings.AddRange(source.Mappings);
        return renamed;
    }

    private void NotifyCanExecuteChanged()
    {
        AddVanillaOverhaulCommand.NotifyCanExecuteChanged();
        AddStoryModOverhaulCommand.NotifyCanExecuteChanged();
        RenameOverhaulCommand.NotifyCanExecuteChanged();
        DeleteOverhaulCommand.NotifyCanExecuteChanged();
        SelectOverhaulCommand.NotifyCanExecuteChanged();
    }

    private enum SourceKind
    {
        Vanilla,
        StoryMod,
    }
}
