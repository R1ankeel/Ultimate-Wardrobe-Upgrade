namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// Headless <see cref="IAppDialogService"/> for tests (Phase 6 amendment 2): never shows UI.
/// Folder picking returns <see langword="null"/> (cancel), confirms return <see langword="false"/>,
/// alerts are no-ops, so view models can be exercised without a dispatcher or WPF-UI host.
/// </summary>
public sealed class NullAppDialogService : IAppDialogService
{
    public Task<string?> PickProjectFolderAsync(string title, string initialDirectory)
        => Task.FromResult<string?>(null);

    public Task<string?> PickFolderAsync(string title, string initialDirectory)
        => Task.FromResult<string?>(null);

    public Task<string?> PromptTextAsync(string title, string message, string? initialValue = null)
        => Task.FromResult<string?>(initialValue);

    public Task<bool> ConfirmAsync(string title, string message)
        => Task.FromResult(false);

    public Task AlertAsync(string title, string message)
        => Task.CompletedTask;
}
