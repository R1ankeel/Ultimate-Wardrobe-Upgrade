namespace UltimateWardrobe.App.Services;

/// <summary>
/// The currently-open Overhaul for the matrix screen (Phase 6 Sprint 6.4, amendment 8). The
/// Project screen navigates to the shared <c>OverhaulView</c> with a specific Overhaul; because the
/// page (and its view model) is recreated from DI on every navigation, this singleton carries the
/// selected <see cref="Core.Domain.Overhaul"/> id across navigations. Headless-testable - it holds
/// only a <see cref="Guid"/>.
/// </summary>
public interface IOverhaulSelection
{
    Guid? OverhaulId { get; }

    void Select(Guid overhaulId);

    void Clear();
}

/// <summary>
/// Default <see cref="IOverhaulSelection"/>: a singleton App-layer service (Phase 6 Sprint 6.4).
/// </summary>
public sealed class OverhaulSelection : IOverhaulSelection
{
    public Guid? OverhaulId { get; private set; }

    public void Select(Guid overhaulId) => OverhaulId = overhaulId;

    public void Clear() => OverhaulId = null;
}
