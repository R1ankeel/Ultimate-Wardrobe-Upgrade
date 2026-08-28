namespace UltimateWardrobe.Tests.DonorLibrary;

/// <summary>
/// Points at the checked-in donor classification goldens (Sprint 2.5.3) and gates their
/// (re)generation behind the <c>UW_WRITE_GOLDENS</c> environment variable, mirroring the
/// Phase 1 <c>CatalogGoldenData</c> pattern. One snapshot per synthetic archetype under
/// <c>tests/TestData/DonorGolden/</c>.
/// </summary>
internal static class DonorGoldenData
{
    public static string RootTestData =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "TestData"));

    public static string DonorGoldenDirectory => Path.Combine(RootTestData, "DonorGolden");

    public static string GoldenFile(SyntheticDonorArchetype archetype) =>
        Path.Combine(DonorGoldenDirectory, $"{ArchetypeName(archetype)}-donor.json");

    public static string ArchetypeName(SyntheticDonorArchetype archetype) => archetype switch
    {
        SyntheticDonorArchetype.EspFullReplacer => "EspFullReplacer",
        SyntheticDonorArchetype.MeshOnlyReplacer => "MeshOnlyReplacer",
        SyntheticDonorArchetype.BodySlideOnlyPatch => "BodySlideOnlyPatch",
        SyntheticDonorArchetype.PhysicsOnlyPatch => "PhysicsOnlyPatch",
        _ => throw new ArgumentOutOfRangeException(nameof(archetype)),
    };

    public static bool ShouldWriteGoldens =>
        string.Equals(Environment.GetEnvironmentVariable("UW_WRITE_GOLDENS"), "1", StringComparison.Ordinal);
}
