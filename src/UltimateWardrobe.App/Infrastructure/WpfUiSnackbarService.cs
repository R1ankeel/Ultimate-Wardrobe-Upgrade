using Wpf.Ui.Controls;

namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// <see cref="ISnackbarService"/> backed by the WPF-UI <see cref="Wpf.Ui.ISnackbarService"/>
/// attached to the shell (Phase 6 Sprint 6.1).
/// </summary>
public sealed class WpfUiSnackbarService : ISnackbarService
{
    private readonly Wpf.Ui.ISnackbarService _snackbar;

    public WpfUiSnackbarService(Wpf.Ui.ISnackbarService snackbar)
    {
        _snackbar = snackbar ?? throw new ArgumentNullException(nameof(snackbar));
    }

    public void Show(string title, string message)
    {
        _snackbar.Show(title, message, ControlAppearance.Info, null, TimeSpan.FromSeconds(4));
    }
}
