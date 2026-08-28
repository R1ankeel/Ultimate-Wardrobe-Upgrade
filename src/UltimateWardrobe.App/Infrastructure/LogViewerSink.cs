using Serilog.Core;
using Serilog.Events;

namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// Serilog sink forwarding every rendered log line into the <see cref="ILogViewer"/> ring buffer
/// (Phase 6 Sprint 6.1 / amendment 5). Registered only in the Serilog pipeline, never in the
/// composition root, so headless tests stay independent of Serilog.
/// </summary>
public sealed class LogViewerSink : ILogEventSink
{
    private readonly ILogViewer _viewer;

    public LogViewerSink(ILogViewer viewer)
    {
        _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null)
        {
            return;
        }

        string rendered = logEvent.RenderMessage();
        _viewer.Append(rendered);
    }
}