namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// UI-agnostic dialog abstraction for view models (Phase 6 amendment 2). The WPF-UI implementation
/// routes alerts/confirms through the <c>ContentDialogHost</c> when attached and falls back to the
/// system <c>MessageBox</c> before the shell is shown; headless tests inject a stub.
/// </summary>
public interface IAppDialogService
{
    Task<string?> PickProjectFolderAsync(string title, string initialDirectory);

    Task<string?> PickFolderAsync(string title, string initialDirectory);

    /// <summary>Picks one donor mod archive (.7z / .zip / .rar). Returns null on cancel.</summary>
    Task<string?> PickModArchiveAsync(string title);

    Task<string?> PromptTextAsync(string title, string message, string? initialValue = null);

    Task<bool> ConfirmAsync(string title, string message);

    Task AlertAsync(string title, string message);
}