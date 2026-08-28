namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// <see cref="IAppNavigationService"/> delegating to the WPF-UI <c>Wpf.Ui.INavigationService</c>
/// bound to the shell <c>NavigationView</c> (Phase 6 Sprint 6.1).
/// </summary>
public sealed class AppNavigationService : IAppNavigationService
{
    private readonly Wpf.Ui.INavigationService _navigation;

    public AppNavigationService(Wpf.Ui.INavigationService navigation)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }

    public bool Navigate(Type pageType)
    {
        return _navigation.Navigate(pageType);
    }

    public bool GoBack()
    {
        return _navigation.GoBack();
    }
}