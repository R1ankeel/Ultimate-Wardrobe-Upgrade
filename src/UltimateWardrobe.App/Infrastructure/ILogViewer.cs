using System.Collections.ObjectModel;

namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// Thread-safe in-memory ring buffer of log lines backing the status bar (Phase 6 Sprint 6.1).
/// Appends raised by <see cref="Append"/> are marshaled to the application dispatcher when one
/// exists so UI subscribers mutate on the UI thread; headless hosts (tests) run without a
/// dispatcher and append inline.
/// </summary>
public interface ILogViewer
{
    ObservableCollection<string> Lines { get; }

    string? LatestLine { get; }

    event EventHandler? LineAppended;

    void Append(string line);

    void Clear();
}