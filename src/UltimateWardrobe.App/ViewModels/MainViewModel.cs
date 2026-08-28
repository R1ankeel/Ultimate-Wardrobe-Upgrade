using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateWardrobe.App.Infrastructure;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Shell-level state: window title, busy flag with a placeholder cancel command, and the live
/// status text fed by <see cref="ILogViewer"/> (Phase 6 Sprint 6.1). The command set is a minimal
/// scaffold - real cancels land with the long-running operations in later sprints.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly ILogViewer _logViewer;
    private readonly IBackgroundTaskService _backgroundTasks;
    private bool _isBusy;
    private string? _statusText;

    public MainViewModel(ILogViewer logViewer, IBackgroundTaskService backgroundTasks)
    {
        _logViewer = logViewer ?? throw new ArgumentNullException(nameof(logViewer));
        _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));

        _logViewer.LineAppended += OnLineAppended;
        _statusText = logViewer.LatestLine ?? "Ready";

        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
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

    public IRelayCommand CancelCommand { get; }

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

    private static string BuildTitle()
    {
        const string appName = "Ultimate Wardrobe";
        var version = typeof(MainViewModel).Assembly.GetName().Version;
        return version is null ? appName : $"{appName} {version.Major}.{version.Minor}.{version.Build}";
    }
}