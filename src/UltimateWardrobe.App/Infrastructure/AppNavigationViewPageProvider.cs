using Microsoft.Extensions.DependencyInjection;

namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// <see cref="Wpf.Ui.Abstractions.INavigationViewPageProvider"/> resolving page instances from the
/// composition root, so every page got DI-constructed with its view model (Phase 6 Sprint 6.1).
/// WPF-UI 4.x renamed the planner's "IPageService" to this abstraction.
/// </summary>
public sealed class AppNavigationViewPageProvider : Wpf.Ui.Abstractions.INavigationViewPageProvider
{
    private readonly IServiceProvider _services;

    public AppNavigationViewPageProvider(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public object GetPage(Type pageType)
    {
        return _services.GetRequiredService(pageType);
    }
}