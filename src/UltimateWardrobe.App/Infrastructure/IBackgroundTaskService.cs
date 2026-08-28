using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// Background work abstraction for view models (Phase 6 amendment 2). Long-running operations are
/// pushed to the thread pool via <c>Task.Run</c> and awaited on the caller context, so the UI
/// thread stays responsive while <c>await</c> continues back on it. Headless tests use the real
/// implementation - it has no WPF dependency.
/// </summary>
public interface IBackgroundTaskService
{
    Task RunAsync(string operationName, Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IBackgroundTaskService"/>: logs start/end around a thread-pool work item and
/// rethrows non-cancellation failures after logging them.
/// </summary>
public sealed class DispatcherBackgroundTaskService : IBackgroundTaskService
{
    private readonly ILogger<DispatcherBackgroundTaskService> _logger;

    public DispatcherBackgroundTaskService(ILogger<DispatcherBackgroundTaskService>? logger = null)
    {
        _logger = logger ?? NullLogger<DispatcherBackgroundTaskService>.Instance;
    }

    public async Task RunAsync(string operationName, Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        _logger.LogInformation("Background operation '{Operation}' started.", operationName);
        try
        {
            await Task.Run(() => work(cancellationToken), cancellationToken).ConfigureAwait(true);
            _logger.LogInformation("Background operation '{Operation}' completed.", operationName);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Background operation '{Operation}' cancelled.", operationName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background operation '{Operation}' failed.", operationName);
            throw;
        }
    }
}