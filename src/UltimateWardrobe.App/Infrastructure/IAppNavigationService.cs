namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// UI-agnostic navigation abstraction for view models (Phase 6 amendment 2). The WPF-UI
/// implementation delegates to <c>Wpf.Ui.INavigationService</c>; headless tests inject a stub.
/// </summary>
public interface IAppNavigationService
{
    bool Navigate(Type pageType);

    bool GoBack();
}