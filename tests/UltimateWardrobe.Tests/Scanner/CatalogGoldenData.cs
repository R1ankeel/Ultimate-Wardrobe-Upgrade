namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// Points at the checked-in golden assets (Sprint 1.6) and gates their (re)generation behind the
/// <c>UW_WRITE_GOLDENS</c> environment variable. Golden catalogs and the static golden plugin are
/// committed under <c>tests/TestData/</c>.
/// </summary>
internal static class CatalogGoldenData
{
    public static string RootTestData =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "TestData"));

    public static string PluginsDirectory => Path.Combine(RootTestData, "Plugins");

    public static string MiniUniversePlugin => Path.Combine(PluginsDirectory, SyntheticSkyrimMods.MiniUniverseFileName);

    public static string CatalogGoldenDirectory => Path.Combine(RootTestData, "CatalogGolden");

    public static string MiniUniverseCatalog => Path.Combine(CatalogGoldenDirectory, "MiniUniverse-catalog.json");

    public static bool ShouldWriteGoldens =>
        string.Equals(Environment.GetEnvironmentVariable("UW_WRITE_GOLDENS"), "1", StringComparison.Ordinal);
}