namespace UltimateWardrobe.Scanner;

public sealed class CatalogScanException : Exception
{
    /// <summary>
    /// EditorId of the record the exception relates to, when the failure has record context.
    /// </summary>
    public string? EditorId { get; init; }

    public CatalogScanException(string message) : base(message)
    {
    }

    public CatalogScanException(string message, Exception innerException) : base(message, innerException)
    {
    }
}