using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// Default <see cref="ILogViewer"/>: keeps the last 1000 lines, exposes the latest line and raises
/// <see cref="ILogViewer.LineAppended"/> so the status bar can display live progress (Phase 6
/// Sprint 6.1). All mutations and the event are marshaled to <see cref="Application.Current"/>
/// dispatcher when available, otherwise executed on the caller thread.
/// </summary>
public sealed class LogViewer : ILogViewer
{
    private const int MaxLines = 1000;

    private readonly object _gate = new();
    private readonly Dispatcher? _dispatcher;

    public LogViewer()
    {
        _dispatcher = Application.Current?.Dispatcher;
    }

    public ObservableCollection<string> Lines { get; } = new();

    public string? LatestLine { get; private set; }

    public event EventHandler? LineAppended;

    public void Append(string line)
    {
        var action = () =>
        {
            lock (_gate)
            {
                Lines.Add(line);
                LatestLine = line;
                while (Lines.Count > MaxLines)
                {
                    Lines.RemoveAt(0);
                }
            }

            LineAppended?.Invoke(this, EventArgs.Empty);
        };

        if (_dispatcher is not null && !_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    public void Clear()
    {
        var action = () =>
        {
            lock (_gate)
            {
                Lines.Clear();
                LatestLine = null;
            }
        };

        if (_dispatcher is not null && !_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }
}