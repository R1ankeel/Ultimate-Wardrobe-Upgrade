using System.Windows;
using System.Windows.Media;
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
    private readonly Wpf.Ui.ISnackbarService _snackbarService;

    public MainWindow(
        MainViewModel viewModel,
        Wpf.Ui.INavigationService navigationService,
        INavigationViewPageProvider pageProvider,
        Wpf.Ui.ISnackbarService snackbarService)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _pageProvider = pageProvider ?? throw new ArgumentNullException(nameof(pageProvider));
        _snackbarService = snackbarService ?? throw new ArgumentNullException(nameof(snackbarService));
        DataContext = viewModel;
        InitializeComponent();
        ApplyAppIcon();
        _snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Sets the app icon from the app-scope resource (Sprint 6.6 polish, roadmap 8.5). Done from
    /// code, not via a <c>{StaticResource AppIcon}</c> XAML attribute: a StaticResource on the root
    /// element can only see resources declared before that point, so it could never resolve the
    /// window's own siblings - and a parse-time failure there crashed the shell right after the
    /// picker created the project. Lookup is null-safe so the shell still builds without an
    /// <see cref="Application"/> (headless STA boot check).
    /// </summary>
    private void ApplyAppIcon()
    {
        var icon = Application.Current?.TryFindResource("AppIcon") as ImageSource;
        if (icon is not null)
        {
            Icon = icon;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeNavigation();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
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
