using System.Text.Json;
using System.Text.Json.Nodes;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;
using UltimateWardrobe.Scanner;
using Xunit.Abstractions;

namespace UltimateWardrobe.Tests.DonorLibrary;

/// <summary>
/// Sprint 2.5.3 golden classification snapshots for the four synthetic donor archetypes. Each
/// snapshot lives under <c>tests/TestData/DonorGolden/</c> and is regenerated intentionally by
/// running with <c>UW_WRITE_GOLDENS=1</c> (review the diff before committing), mirroring the
/// Phase 1 catalog goldens.
/// </summary>
public class DonorGoldenTests : IDisposable
{
    private const string NormalizedRootPath = "<root>";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"UW_Donor_Gold_{Guid.NewGuid():N}");
    private readonly ITestOutputHelper _output;

    public DonorGoldenTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>
    /// Serializes a classified <see cref="DonorAsset"/> with the dynamic <c>extractedPath</c>
    /// normalized to a fixed placeholder and the nondeterministic <c>importedAt</c> timestamp
    /// removed, so the golden output is reproducible across machines, temp dirs, and runs. The
    /// classifier's deterministic placeholder archive hash and fixed-Guid ImportId are kept
    /// (they are part of the classifier contract).
    /// </summary>
    private static string Serialize(DonorAsset donor)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(donor, CatalogCacheStore.JsonOptions))!.AsObject();
        node["extractedPath"] = NormalizedRootPath;
        node.Remove("importedAt");
        return node.ToJsonString();
    }

    [Theory]
    [MemberData(nameof(Archetypes))]
    public async Task Synthetic_Archetype_Matches_CommittedGolden(SyntheticDonorArchetype archetype)
    {
        var dir = SyntheticDonorUniverse.Write(_root, archetype);

        var donor = await new DonorClassifier().ClassifyAsync(dir);
        var json = Serialize(donor);

        var goldenFile = DonorGoldenData.GoldenFile(archetype);
        if (DonorGoldenData.ShouldWriteGoldens)
        {
            Directory.CreateDirectory(DonorGoldenData.DonorGoldenDirectory);
            File.WriteAllText(goldenFile, json);
            _output.WriteLine($"Golden written: {goldenFile}. Rerun without UW_WRITE_GOLDENS to verify.");
            return;
        }

        Assert.True(
            File.Exists(goldenFile),
            $"Golden missing at '{goldenFile}'. Generate with UW_WRITE_GOLDENS=1.");
        var golden = File.ReadAllText(goldenFile);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(golden)),
            $"Archetype '{DonorGoldenData.ArchetypeName(archetype)}' diverged from its committed golden. To refresh intentionally, rerun with UW_WRITE_GOLDENS=1 and review the diff.");
    }

    [Theory]
    [MemberData(nameof(Archetypes))]
    public async Task Synthetic_Archetype_Classifies_To_Expected_Kind(SyntheticDonorArchetype archetype)
    {
        var dir = SyntheticDonorUniverse.Write(_root, archetype);

        var donor = await new DonorClassifier().ClassifyAsync(dir);

        var expected = ExpectedKind(archetype);
        Assert.Equal(expected, donor.Kind);
    }

    [Fact]
    public async Task Golden_Serialization_Is_Byte_Deterministic()
    {
        foreach (var archetype in SyntheticDonorUniverse.All)
        {
            var dir = SyntheticDonorUniverse.Write(_root, archetype);
            var a = await new DonorClassifier().ClassifyAsync(dir);
            var b = await new DonorClassifier().ClassifyAsync(dir);

            Assert.Equal(Serialize(a), Serialize(b));
        }
    }

    public static IEnumerable<object[]> Archetypes =>
        SyntheticDonorUniverse.All.Select(a => new object[] { a });

    private static DonorAssetKind ExpectedKind(SyntheticDonorArchetype archetype) => archetype switch
    {
        SyntheticDonorArchetype.EspFullReplacer => DonorAssetKind.FullReplacer,
        SyntheticDonorArchetype.MeshOnlyReplacer => DonorAssetKind.FullReplacer,
        SyntheticDonorArchetype.BodySlideOnlyPatch => DonorAssetKind.BodyConversionPatch,
        SyntheticDonorArchetype.PhysicsOnlyPatch => DonorAssetKind.PhysicsPatch,
        _ => throw new ArgumentOutOfRangeException(nameof(archetype)),
    };
}
