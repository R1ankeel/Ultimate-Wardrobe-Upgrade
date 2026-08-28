using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Storage;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Shell-level state (Phase 6 Sprint 6.1): window title, busy flag with a placeholder cancel
/// command, and the live status text fed by <see cref="ILogViewer"/>. Sprint 6.6 polish adds the
/// persisted dark/light theme toggle (roadmap 8.5) applied through <see cref="IThemeService"/>.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly ILogViewer _logViewer;
    private readonly IBackgroundTaskService _backgroundTasks;
    private readonly IThemeService _theme;
    private bool _isBusy;
    private string? _statusText;
    private bool _isDarkTheme;

    public MainViewModel(ILogViewer logViewer, IBackgroundTaskService backgroundTasks, IThemeService theme)
    {
        _logViewer = logViewer ?? throw new ArgumentNullException(nameof(logViewer));
        _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));

        _logViewer.LineAppended += OnLineAppended;
        _statusText = logViewer.LatestLine ?? "Ready";
        _isDarkTheme = !string.Equals(theme.ThemeMode, RecentProjectsStore.LightTheme, StringComparison.OrdinalIgnoreCase);

        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
    }

    public string Title => BuildTitle();

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set => SetProperty(ref _isDarkTheme, value);
    }

    /// <summary>Button label for the shell theme toggle (roadmap 8.5).</summary>
    public string ThemeLabel => IsDarkTheme ? "Light" : "Dark";

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ToggleThemeCommand { get; }

    private void OnLineAppended(object? sender, EventArgs e)
    {
        StatusText = _logViewer.LatestLine;
    }

    private void Cancel()
    {
        _backgroundTasks.RunAsync(
            "Cancel placeholder",
            _ => Task.CompletedTask,
            CancellationToken.None);
    }

    private void ToggleTheme()
    {
        var next = IsDarkTheme ? RecentProjectsStore.LightTheme : RecentProjectsStore.DarkTheme;
        _theme.Apply(next);
        IsDarkTheme = !IsDarkTheme;
        OnPropertyChanged(nameof(ThemeLabel));
    }

    private static string BuildTitle()
    {
        const string appName = "Ultimate Wardrobe";
        var version = typeof(MainViewModel).Assembly.GetName().Version;
        return version is null ? appName : $"{appName} {version.Major}.{version.Minor}.{version.Build}";
    }
}