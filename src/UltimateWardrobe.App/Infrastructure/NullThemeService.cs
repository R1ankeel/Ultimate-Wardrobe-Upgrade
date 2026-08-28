namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// In-memory <see cref="IThemeService"/> for headless tests (Phase 6 Sprint 6.6 polish). No WPF
/// dependency - just records the current mode and applies (no-ops) on every call.
/// </summary>
public sealed class NullThemeService : IThemeService
{
    public NullThemeService(string themeMode = "Dark")
    {
        _themeMode = themeMode;
    }

    private string _themeMode;

    public string ThemeMode => _themeMode;

    public void Apply(string themeMode)
    {
        if (string.IsNullOrWhiteSpace(themeMode))
        {
            throw new ArgumentException("Theme mode must not be empty.", nameof(themeMode));
        }
        _themeMode = themeMode;
    }
}
