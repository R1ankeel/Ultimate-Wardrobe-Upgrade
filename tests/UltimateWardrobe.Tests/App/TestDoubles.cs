using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Persistence;
using FluentAssertions;
using System.Reflection;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Scriptable <see cref="IAppDialogService"/> for headless ViewModel tests (Phase 6 Sprint 6.2).
/// Each picker/prompt is backed by a delegate you override per test; alerts/confirms are recorded so
/// a test can assert what was surfaced without any UI.
/// </summary>
internal sealed class ScriptedDialogService : IAppDialogService
{
    public Func<string?, string?, string?> PickProjectFolder = (_, _) => null;
    public Func<string?, string?, string?> PickFolder = (_, _) => null;
    public Func<string?, string?, string?, string?> PromptText = (_, _, _) => null;
    public bool ConfirmResult = true;

    public List<(string Title, string Message)> Alerts { get; } = new();
    public List<(string Title, string Message)> Confirms { get; } = new();
    public int FolderPickCount;

    public Task<string?> PickProjectFolderAsync(string title, string initialDirectory)
        => Task.FromResult(PickProjectFolder(title, initialDirectory));

    public Task<string?> PickFolderAsync(string title, string initialDirectory)
    {
        FolderPickCount++;
        return Task.FromResult(PickFolder(title, initialDirectory));
    }

    public Task<string?> PromptTextAsync(string title, string message, string? initialValue = null)
        => Task.FromResult(PromptText(title, message, initialValue));

    public Task<bool> ConfirmAsync(string title, string message)
    {
        Confirms.Add((title, message));
        return Task.FromResult(ConfirmResult);
    }

    public Task AlertAsync(string title, string message)
    {
        Alerts.Add((title, message));
        return Task.CompletedTask;
    }
}

/// <summary>
/// <see cref="IProjectStore"/> stub that records calls and returns a preset project from
/// <see cref="LoadAsync"/> (Phase 6 Sprint 6.2).
/// </summary>
internal sealed class RecordingStore : IProjectStore
{
    public int SaveCount;
    public readonly List<Project> SavedProjects = new();

    public Task SaveAsync(Project project, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        SavedProjects.Add(project);
        return Task.CompletedTask;
    }

    public Task<Project> LoadAsync(string projectDbPath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Loading through the stub store is not used by Sprint 6.2 tests.");
}

/// <summary>
/// <see cref="IProjectStoreFactory"/> stub returning a shared <see cref="RecordingStore"/>.
/// </summary>
internal sealed class RecordingStoreFactory : IProjectStoreFactory
{
    public RecordingStore Store { get; } = new();

    public IProjectStore Open(string projectDbPath) => Store;
}

/// <summary>
/// <see cref="IAppNavigationService"/> stub recording every <see cref="IAppNavigationService.Navigate"/>
/// target (Phase 6 Sprint 6.2).
/// </summary>
internal sealed class RecordingNavigation : IAppNavigationService
{
    public List<Type> Navigated { get; } = new();

    public bool Navigate(Type pageType)
    {
        Navigated.Add(pageType);
        return true;
    }

    public bool GoBack() => false;
}

/// <summary>
/// Reflection guard (Phase 6 Sprint 6.2): guarantees a view model does not leak a switch/close
/// project command into the shell - such shell-level project lifecycle belongs to the picker window,
/// not to per-screen view models.
/// </summary>
internal static class CommandNameLeak
{
    private static readonly string[] Banned = { "SwitchProject", "CloseProject", "SwitchProjectCommand", "CloseProjectCommand" };

    public static void Check(Type viewModelType)
    {
        var commands = viewModelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.EndsWith("Command", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToList();

        var leaked = commands.Where(name => Banned.Any(name.Contains)).ToList();
        leaked.Should().BeEmpty(
            $"{viewModelType.Name} must not expose a shell-level switch/close project command (found: {string.Join(", ", leaked)}).");
    }
}
