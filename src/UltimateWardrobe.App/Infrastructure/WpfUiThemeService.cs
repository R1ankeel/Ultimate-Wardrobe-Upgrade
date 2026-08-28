using Wpf.Ui.Appearance;

namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// Default <see cref="IThemeService"/> for the WPF shell: reads the persisted preference on
/// construction, applies it through <c>ApplicationThemeManager</c> and persists every toggle
/// (Phase 6 Sprint 6.6 polish, roadmap 8.5).
/// </summary>
public sealed class WpfUiThemeService : IThemeService
{
    private readonly Storage.RecentProjectsStore _store;
    private string _themeMode;

    public WpfUiThemeService(Storage.RecentProjectsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _themeMode = _store.GetThemeMode();
        ApplyThemeInternal(_themeMode);
    }

    public string ThemeMode => _themeMode;

    public void Apply(string themeMode)
    {
        if (string.IsNullOrWhiteSpace(themeMode))
        {
            throw new ArgumentException("Theme mode must not be empty.", nameof(themeMode));
        }

        _themeMode = themeMode;
        _store.SetThemeMode(_themeMode);
        ApplyThemeInternal(_themeMode);
    }

    private static void ApplyThemeInternal(string themeMode)
    {
        var theme = string.Equals(themeMode, Storage.RecentProjectsStore.LightTheme, StringComparison.OrdinalIgnoreCase)
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(theme);
    }
}
