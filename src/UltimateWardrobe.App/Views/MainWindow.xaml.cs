using System.Windows;
using UltimateWardrobe.App.ViewModels;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// The shell (Phase 6 Sprint 6.1): a <see cref="FluentWindow"/> with a <see cref="NavigationView"/>
/// pane (Project / Overhaul / Export - the roadmap 8.2 screens in scope, minus a shell project-list
/// page per amendment 7) and a status bar (busy spinner + <see cref="ILogViewer"/> line + cancel) and
/// the version in the title. On <c>Loaded</c> the WPF-UI page provider is attached to the
/// <c>NavigationView</c> (so item clicks resolve pages from DI) and the programmatic navigation
/// service is bound to the same control.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly Wpf.Ui.INavigationService _navigationService;
    private readonly INavigationViewPageProvider _pageProvider;

    public MainWindow(
        MainViewModel viewModel,
        Wpf.Ui.INavigationService navigationService,
        INavigationViewPageProvider pageProvider)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _pageProvider = pageProvider ?? throw new ArgumentNullException(nameof(pageProvider));
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeNavigation();
    }

    /// <summary>
    /// Wires the WPF-UI 4.3.0 services to the shell's <see cref="NavigationView"/>: attaches the
    /// DI page provider (so navigation items resolve pages from the composition root) and binds the
    /// programmatic <c>INavigationService</c> to the same control, then navigates to the Project
    /// screen. Called on <c>Loaded</c>; public so the headless boot verification can invoke it.
    /// </summary>
    public void InitializeNavigation()
    {
        RootNavigation.SetPageProviderService(_pageProvider);
        _navigationService.SetNavigationControl(RootNavigation);
        RootNavigation.Navigate(typeof(ProjectView));
    }
}
