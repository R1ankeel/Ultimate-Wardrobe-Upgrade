using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;
using Xunit.Abstractions;

namespace UltimateWardrobe.Tests.DonorLibrary;

/// <summary>
/// Sprint 2.5.2 real-donor integration coverage, gated behind the Integration category and
/// auto-skipping (with an output note) whenever <c>ModsForTests/Armor</c> is absent. The
/// shortlist was chosen at execution time after gate 2.5.2b (see <c>Plans/phase2.md</c> Execution
/// Log): extracting every candidate proved that "Red Hood - Main File" and "Nightshade CBBE 3BA"
/// both ship an esp (branch 1), while the genuinely esp-less branch-2 donor is "Red Hood - HIMBO".
/// Each real donor is classified with the vanilla <see cref="VanillaCatalogSource"/> as the
/// reference-carrying hint, asserted loosely (Kind != Unknown OR >= 1 ProvidedSet OR flags
/// non-empty), diagnosed, and cleaned up.
/// </summary>
[Trait("Category", "Integration")]
public class RealDonorIntegrationTests
{
    private const string GameRoot = @"D:\Skymod\Stock Game";

    private static string ArmorDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ModsForTests", "Armor"));

    private static string? FindArmorArchive(string namePart)
    {
        if (!Directory.Exists(ArmorDir))
        {
            return null;
        }

        return Directory.EnumerateFiles(ArmorDir)
            .FirstOrDefault(f => Path.GetFileName(f).Contains(namePart, StringComparison.OrdinalIgnoreCase));
    }

    private readonly ITestOutputHelper _output;

    public RealDonorIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static Catalog? VanillaHint()
    {
        return Directory.Exists(GameRoot) ? new Catalog(new VanillaCatalogSource(GameRoot), Array.Empty<ArmorSet>()) : null;
    }

    /// <summary>
    /// Extracts the real archive to a temp folder, classifies it with the optional vanilla hint,
    /// prints a diagnostic line, and returns the extracted folder path (caller cleans up).
    /// </summary>
    private async Task<(DonorAsset Asset, string Dest)> Analyze(string archive, Catalog? hint)
    {
        var dest = Path.Combine(Path.GetTempPath(), "UW_Donor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);

        var imported = await new DonorImportService().ImportAsync(archive, dest);
        var asset = await new DonorClassifier().ClassifyAsync(imported.ExtractedPath, hint);

        _output.WriteLine(
            $"[real-donor] {Path.GetFileName(archive)} -> Kind={asset.Kind} | sets={asset.ProvidedSets.Count}" +
            $" | bodySlide={asset.DetectedBodySlideFiles.Count} physics={asset.DetectedPhysicsFiles.Count} | files={asset.FileManifest.Count} | path={asset.ExtractedPath}");

        return (asset, dest);
    }

    private static void LooseAssert(DonorAsset asset)
    {
        Assert.True(
            asset.Kind != DonorAssetKind.Unknown
            || asset.ProvidedSets.Count >= 1
            || asset.DetectedBodySlideFiles.Count > 0
            || asset.DetectedPhysicsFiles.Count > 0,
            $"Real donor classified to nothing: Kind={asset.Kind}, sets={asset.ProvidedSets.Count}, flags empty.");
        Assert.NotEmpty(asset.FileManifest);
    }

    [Fact]
    public async Task EspFullReplacer_RangersArmor_Classifies()
    {
        var archive = FindArmorArchive("Rangers Armor 185396");
        if (archive is null || !File.Exists(archive))
        {
            _output.WriteLine($"Skipped: no 'Rangers Armor' archive under ModsForTests/Armor.");
            return;
        }

        var (asset, dest) = await Analyze(archive, VanillaHint());
        try
        {
            LooseAssert(asset);
            Assert.NotEmpty(asset.ProvidedSets);
        }
        finally
        {
            Cleanup(dest);
        }
    }

    [Fact]
    public async Task Branch2_MeshOnly_RedHoodHimbo_Classifies()
    {
        var archive = FindArmorArchive("Red Hood - HIMBO");
        if (archive is null || !File.Exists(archive))
        {
            _output.WriteLine($"Skipped: no 'Red Hood - HIMBO' archive under ModsForTests/Armor.");
            return;
        }

        var (asset, dest) = await Analyze(archive, VanillaHint());
        try
        {
            LooseAssert(asset);
        }
        finally
        {
            Cleanup(dest);
        }
    }

    [Fact]
    public async Task BodyConversionPatch_RangersCbbePatch_Classifies()
    {
        var archive = FindArmorArchive("Rangers Armor - CBBE Patch");
        if (archive is null || !File.Exists(archive))
        {
            _output.WriteLine($"Skipped: no 'Rangers Armor - CBBE Patch' archive under ModsForTests/Armor.");
            return;
        }

        var (asset, dest) = await Analyze(archive, VanillaHint());
        try
        {
            LooseAssert(asset);
        }
        finally
        {
            Cleanup(dest);
        }
    }

    [Fact]
    public async Task DetectsPhysicsFlags_RedHoodHimbo_Classifies()
    {
        // Physics flags are attached to folders that already received a ProvidedSet mesh, so a
        // clean standalone 'physics patch' is hard to construct from this corpus. Probing (gate
        // 2.5.2b) showed EBONWRAITH 'HDT SMP Patch' ships only mesh weight variants and Gryphon
        // Knight's .tri files sit in folders that received no set, so both classify to Unknown.
        // 'Red Hood - HIMBO' (esp-less branch 2) genuinely yields physics flags (10 detected), so it
        // is used to exercise the real physics-flagging path (recorded in the Execution Log 2026-08-28).
        var archive = FindArmorArchive("Red Hood - HIMBO");
        if (archive is null || !File.Exists(archive))
        {
            _output.WriteLine($"Skipped: no 'Red Hood - HIMBO' archive under ModsForTests/Armor.");
            return;
        }

        var (asset, dest) = await Analyze(archive, VanillaHint());
        try
        {
            LooseAssert(asset);
            Assert.NotEmpty(asset.DetectedPhysicsFiles);
        }
        finally
        {
            Cleanup(dest);
        }
    }

    [Fact]
    public async Task Gate_Branch2_Fixture_Is_Truly_EspLess()
    {
        var archive = FindArmorArchive("Red Hood - HIMBO");
        if (archive is null || !File.Exists(archive))
        {
            _output.WriteLine($"Skipped: no 'Red Hood - HIMBO' archive under ModsForTests/Armor.");
            return;
        }

        // Gate 2.5.2b: the branch-2 integration fixture must contain NO plugin that would route
        // it to branch 1, so branch 2 is exercised on a REAL esp-less mod, not only synthetic trees.
        var dest = Path.Combine(Path.GetTempPath(), "UW_Donor_Probe_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dest);
            var result = await new CompositeExtractor().ExtractAsync(archive, dest);
            var plugins = Directory.EnumerateFiles(dest, "*.esp", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(dest, "*.esm", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(dest, "*.esl", SearchOption.AllDirectories))
                .ToList();

            Assert.True(result.ExtractedFiles.Count > 0, "Branch-2 fixture extracted no files.");
            Assert.Empty(plugins);
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }
    }

    private static void Cleanup(string dest)
    {
        try { Directory.Delete(dest, true); } catch { }
    }
}
