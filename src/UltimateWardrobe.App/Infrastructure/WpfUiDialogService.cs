using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

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
        return Task.FromResult(PickFolderCore(title, initialDirectory));
    }

    public Task<string?> PickFolderAsync(string title, string initialDirectory)
    {
        return Task.FromResult(PickFolderCore(title, initialDirectory));
    }

    public Task<string?> PromptTextAsync(string title, string message, string? initialValue = null)
    {
        return Task.FromResult(PromptTextWindow(title, message, initialValue));
    }

    private static string? PickFolderCore(string title, string initialDirectory)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : initialDirectory,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string? PromptTextWindow(string title, string message, string? initialValue)
    {
        var textBox = new TextBox { Text = initialValue ?? string.Empty, MinWidth = 320, Margin = new(0, 8, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new(8, 0, 0, 0), IsCancel = true };
        string? result = null;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new(16) };
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(textBox);
        root.Children.Add(buttons);

        var window = new Window
        {
            Title = title,
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current?.MainWindow,
        };
        ok.Click += (_, _) => { result = textBox.Text; window.DialogResult = true; };
        cancel.Click += (_, _) => window.DialogResult = false;

        return window.ShowDialog() == true ? result : null;
    }
}