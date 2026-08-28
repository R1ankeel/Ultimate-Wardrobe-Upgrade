namespace UltimateWardrobe.Patcher;

/// <summary>
/// A typed, build-blocking failure of the Phase 5 export pipeline (plan section 3, amendment #5:
/// for example an Overhaul with no scanned catalog attached, an unreadable source root, or an
/// output folder that cannot be prepared). Per-mapping problems never throw - they surface as
/// <see cref="Core.Abstractions.PatchWarning"/>s and skip only that mapping.
/// </summary>
public sealed class PatchException : Exception
{
    public PatchException(string message) : base(message)
    {
    }

    public PatchException(string message, Exception inner) : base(message, inner)
    {
    }
}