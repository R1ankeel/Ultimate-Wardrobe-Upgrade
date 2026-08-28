namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// UI-agnostic theme switch for the shell (Phase 6 Sprint 6.6 polish, roadmap 8.5). The WPF-UI
/// implementation routes through <c>Wpf.Ui.Appearance.ApplicationThemeManager</c> and persists the
/// choice via <see cref="Storage.RecentProjectsStore"/>; headless tests inject an in-memory stub.
/// </summary>
public interface IThemeService
{
    /// <summary>The currently applied theme mode: "Dark" or "Light".</summary>
    string ThemeMode { get; }

    /// <summary>Applies a theme mode ("Dark" or "Light") and persists it.</summary>
    void Apply(string themeMode);
}
