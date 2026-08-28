namespace UltimateWardrobe.App.Infrastructure;

/// <summary>
/// Headless <see cref="ISnackbarService"/> for tests (Phase 6 amendment 2): no-op sink.
/// </summary>
public sealed class NullSnackbarService : ISnackbarService
{
    public void Show(string title, string message)
    {
    }
}
