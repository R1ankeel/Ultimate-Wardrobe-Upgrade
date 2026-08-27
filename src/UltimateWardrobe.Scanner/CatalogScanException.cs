namespace UltimateWardrobe.Scanner;

public sealed class CatalogScanException : Exception
{
    public CatalogScanException(string message) : base(message)
    {
    }

    public CatalogScanException(string message, Exception innerException) : base(message, innerException)
    {
    }
}