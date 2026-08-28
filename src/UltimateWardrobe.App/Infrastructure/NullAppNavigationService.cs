namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// Headless <see cref="IAppNavigationService"/> for tests (Phase 6 amendment 2): never touches a
/// <c>NavigationView</c>. Navigate reports success, GoBack reports no history.
/// </summary>
public sealed class NullAppNavigationService : IAppNavigationService
{
    public bool Navigate(Type pageType) => true;

    public bool GoBack() => false;
}
