namespace UltimateWardrobe.Persistence;

/// <summary>
/// A typed exception for persistence failures that should surface as a user-friendly message
/// rather than a raw <see cref="System.Data.Common.DbException"/>. Thrown for an unreadable /
/// unopenable <c>project.db</c>, a schema newer than the app understands, or a migration failure.
/// </summary>
public sealed class ProjectStoreException : Exception
{
    public ProjectStoreException(string message)
        : base(message)
    {
    }

    public ProjectStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
