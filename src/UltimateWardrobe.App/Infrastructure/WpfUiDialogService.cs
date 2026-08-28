using System.Windows;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// <see cref="IAppDialogService"/> for the real shell: alerts/confirms go through the WPF-UI
/// <c>ContentDialogHost</c> when one is attached, otherwise fall back to the system message box
/// (startup gate runs before the shell window exists). Folder picking uses the .NET 8
/// <c>Microsoft.Win32.OpenFolderDialog</c> - WPF-UI 4.3 ships no folder picker (Phase 6 Sprint 6.1
/// spike conclusion).
/// </summary>
public sealed class WpfUiDialogService : IAppDialogService
{
    private readonly Wpf.Ui.IContentDialogService _dialogs;

    public WpfUiDialogService(Wpf.Ui.IContentDialogService dialogs)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (_dialogs.GetDialogHostEx() is null)
        {
            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            return result == MessageBoxResult.OK;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        var dialogResult = await _dialogs.ShowAsync(dialog, CancellationToken.None);
        return dialogResult == ContentDialogResult.Primary;
    }

    public async Task AlertAsync(string title, string message)
    {
        if (_dialogs.GetDialogHostEx() is null)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await _dialogs.ShowAlertAsync(title, message, "OK");
    }

    public Task<string?> PickProjectFolderAsync(string title, string initialDirectory)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : initialDirectory,
        };

        return Task.FromResult<string?>(dialog.ShowDialog() == true ? dialog.FolderName : null);
    }
}