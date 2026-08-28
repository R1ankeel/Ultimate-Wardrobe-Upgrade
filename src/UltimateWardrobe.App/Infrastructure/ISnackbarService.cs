namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// UI-agnostic snackbar abstraction for view models (Phase 6 amendment 2). The WPF-UI
/// implementation delegates to <c>Wpf.Ui.ISnackbarService</c>; headless tests inject a no-op stub.
/// </summary>
public interface ISnackbarService
{
    void Show(string title, string message);
}
