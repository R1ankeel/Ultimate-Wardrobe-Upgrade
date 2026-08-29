using System.Windows;
using UltimateWardrobe.App.ViewModels;
using Wpf.Ui.Controls;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// The startup gate (Phase 6 amendment 7): hosts <see cref="ProjectListViewModel"/> and shows
/// BEFORE the shell. A successful pick/create publishes the single open project on
/// <see cref="Services.IProjectSession"/> and raises <c>CloseRequested</c>, so <c>App.OnStartup</c>
/// can then build <see cref="MainWindow"/>. Canceling (closing) this window ends the app with no
/// shell - <c>App</c> reacts to the session not being open.
/// </summary>
public partial class ProjectPickerWindow : FluentWindow
{
    private readonly ProjectListViewModel _viewModel;

    public ProjectPickerWindow(ProjectListViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        InitializeComponent();
        _viewModel.CloseRequested += OnCloseRequested;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private void OnCloseRequested()
    {
        Dispatcher.Invoke(() => Close());
    }
}
