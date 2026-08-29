using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.ObjectModel;
using System.IO;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.Storage;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Persistence;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>One <c>project.db</c> path in the picker's recent list (Phase 6 Sprint 6.1).</summary>
public sealed record RecentProjectItem(string Path, string Name);

/// <summary>
/// Startup gate view model (Phase 6 amendment 7): lists recent projects, creates or opens a single
/// project through <see cref="IProjectStoreFactory"/>, records it in
/// <see cref="RecentProjectsStore"/>, publishes it on <see cref="IProjectSession"/>, then raises
/// <see cref="CloseRequested"/> so the picker window closes and the shell can show. Heavily logged;
/// every failure surfaces as an alert and leaves the session untouched.
/// </summary>
public sealed class ProjectListViewModel : ObservableObject
{
    private readonly RecentProjectsStore _recentStore;
    private readonly IProjectStoreFactory _storeFactory;
    private readonly IProjectSession _session;
    private readonly IAppDialogService _dialogs;
    private readonly ILogger<ProjectListViewModel> _logger;
    private RecentProjectItem? _selectedRecent;
    private bool _isBusy;
    private IAsyncRelayCommand? _newProjectCommand;
    private IAsyncRelayCommand? _openProjectCommand;
    private IAsyncRelayCommand? _openSelectedRecentCommand;
    private IAsyncRelayCommand<RecentProjectItem>? _deleteRecentCommand;

    public ProjectListViewModel(
        RecentProjectsStore recentStore,
        IProjectStoreFactory storeFactory,
        IProjectSession session,
        IAppDialogService dialogs,
        ILogger<ProjectListViewModel>? logger = null)
    {
        _recentStore = recentStore ?? throw new ArgumentNullException(nameof(recentStore));
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _logger = logger ?? NullLogger<ProjectListViewModel>.Instance;
    }

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = new();

    public RecentProjectItem? SelectedRecent
    {
        get => _selectedRecent;
        set => SetProperty(ref _selectedRecent, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Raised once a project is open so the hosting window can close itself.</summary>
    public event Action? CloseRequested;

    public IAsyncRelayCommand NewProjectCommand =>
        _newProjectCommand ??= new AsyncRelayCommand(
            () => PickAndOpenAsync(createIfMissing: true));

    public IAsyncRelayCommand OpenProjectCommand =>
        _openProjectCommand ??= new AsyncRelayCommand(
            () => PickAndOpenAsync(createIfMissing: false));

    public IAsyncRelayCommand OpenSelectedRecentCommand =>
        _openSelectedRecentCommand ??= new AsyncRelayCommand(
            () =>
            {
                var selected = SelectedRecent;
                if (selected is null)
                {
                    return Task.CompletedTask;
                }

                // The recent list stores the project.db path (Sprint 6.1); the open flow roots on the
                // project folder, so derive it. Opening a recent entry recreates a missing project.db
                // instead of alerting (user finding - a project must stay reachable even without a
                // database, e.g. after the old db was deleted or never materialized).
                var directory = Path.GetDirectoryName(selected.Path);
                return string.IsNullOrEmpty(directory)
                    ? Task.CompletedTask
                    : OpenRootAsync(directory, createIfMissing: true);
            });

    /// <summary>
    /// Deletes a recent project from disk (per-row, Sprint 6.7): confirms first, then removes the
    /// project folder recursively and forgets the recent entry. A failed deletion surfaces an alert
    /// and keeps the entry so the user can retry.
    /// </summary>
    public IAsyncRelayCommand<RecentProjectItem> DeleteRecentCommand =>
        _deleteRecentCommand ??= new AsyncRelayCommand<RecentProjectItem>(
            DeleteProjectAsync,
            item => item is not null);

    public Task InitializeAsync()
    {
        RecentProjects.Clear();
        foreach (var path in _recentStore.GetRecentProjectPaths())
        {
            RecentProjects.Add(CreateItem(path));
        }

        _logger.LogInformation("Recent projects loaded: {Count}.", RecentProjects.Count);
        return Task.CompletedTask;
    }

    private static RecentProjectItem CreateItem(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        var name = string.IsNullOrEmpty(directory)
            ? Path.GetFileName(databasePath)
            : Path.GetFileName(directory);
        return new RecentProjectItem(databasePath, name);
    }

    private async Task DeleteProjectAsync(RecentProjectItem? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        var directory = Path.GetDirectoryName(item.Path);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Delete project",
            $"Delete project '{item.Name}'? This permanently removes the project folder '{directory}' and all its data.");
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            _recentStore.RemoveRecentProject(item.Path);
            RecentProjects.Remove(item);
            if (ReferenceEquals(SelectedRecent, item))
            {
                SelectedRecent = RecentProjects.FirstOrDefault();
            }

            _logger.LogInformation("Deleted project '{Name}' at '{Directory}'.", item.Name, directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not delete project '{Name}' at '{Directory}'.", item.Name, directory);
            await _dialogs.AlertAsync("Delete failed", $"The project folder could not be deleted: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PickAndOpenAsync(bool createIfMissing)
    {
        var root = await _dialogs.PickProjectFolderAsync(
            createIfMissing ? "New project - choose the project folder" : "Open project - choose the project folder",
            string.Empty);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        await OpenRootAsync(root, createIfMissing);
    }

    public async Task OpenRootAsync(string root, bool createIfMissing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var databasePath = Path.Combine(root, "project.db");
            var databaseExists = File.Exists(databasePath);

            if (!databaseExists && !createIfMissing)
            {
                await _dialogs.AlertAsync(
                    "Project database not found",
                    $"The folder \"{root}\" does not contain a project.db file.");
                return;
            }

            var store = _storeFactory.Open(databasePath);

            if (!databaseExists)
            {
                if (!Directory.Exists(root))
                {
                    Directory.CreateDirectory(root);
                }

                var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var project = new Project(Guid.NewGuid(), name, root);
                _session.Open(project, databasePath, store);
                await _session.Store!.SaveAsync(project);

                _recentStore.AddRecentProject(databasePath);
                _logger.LogInformation("Created project '{Name}' at '{Root}'.", project.Name, root);
            }
            else
            {
                var project = await store.LoadAsync(databasePath);
                _session.Open(project, databasePath, store);
                _recentStore.AddRecentProject(databasePath);
                _logger.LogInformation(
                    "Opened project '{Name}' (db '{Database}').",
                    project.Name,
                    databasePath);
            }

            await InitializeAsync();
            CloseRequested?.Invoke();
        }
        catch (ProjectStoreException ex)
        {
            _logger.LogError(ex, "Failed to open or create the project at '{Root}'.", root);
            await _dialogs.AlertAsync("Could not open the project", ex.Message);
        }
    }
}