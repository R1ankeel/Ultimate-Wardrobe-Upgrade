using System.Diagnostics;
using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;
using Xunit.Abstractions;

namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// Sprint 1.6.5 real-data coverage, gated behind the Integration category and auto-skipping
/// (with an output note) whenever Skyrim / story-mod asset paths are absent on the machine.
/// </summary>
[Trait("Category", "Integration")]
public class RealDataScannerTests
{
    private const string GameRoot = @"D:\Skymod\Stock Game";

    private static string ModsQuestDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ModsForTests", "QuestExpansiaon"));

    private readonly ITestOutputHelper _output;

    public RealDataScannerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Vanilla_RealGame_Scan_ProducesCatalog_InTimeout()
    {
        if (!Directory.Exists(GameRoot))
        {
            _output.WriteLine($"Skipped: Skyrim game root '{GameRoot}' is absent.");
            return;
        }

        var watch = Stopwatch.StartNew();
        var catalog = await new FolderCatalogScanner().ScanAsync(new VanillaCatalogSource(GameRoot));
        watch.Stop();

        _output.WriteLine($"Vanilla scan: {watch.ElapsedMilliseconds} ms; TotalArmo={catalog.Stats.TotalArmo}, GroupedSets={catalog.Stats.GroupedSets}, Warnings={catalog.Warnings.Count}");

        Assert.True(catalog.Stats.TotalArmo > 500, $"Expected TotalArmo > 500, got {catalog.Stats.TotalArmo}.");
        Assert.True(catalog.Stats.GroupedSets > 50, $"Expected GroupedSets > 50, got {catalog.Stats.GroupedSets}.");
        Assert.True(
            catalog.Stats.SkippedByReason.GetValueOrDefault(SkipReason.CreatureRace) >= 1,
            $"Expected at least one CreatureRace skip (Boar/Chaurus-style skins), got {catalog.Stats.SkippedByReason.GetValueOrDefault(SkipReason.CreatureRace)}.");

        var iron = catalog.Sets.FirstOrDefault(s => s.Id.Contains("iron", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(iron);
        Assert.True(iron!.Variants.Any(v => v.Pieces.Count >= 1),
            "Iron set exists but contains no pieces.");

        Assert.True(watch.ElapsedMilliseconds <= 10_000, $"Vanilla scan took {watch.ElapsedMilliseconds} ms, expected <= 10 s.");
    }

    [Fact]
    public async Task StoryMod_Vigilant_Scan_ProducesSets()
    {
        if (!Directory.Exists(ModsQuestDir))
        {
            _output.WriteLine($"Skipped: 'ModsForTests/QuestExpansiaon' is absent.");
            return;
        }

        var rar = Directory.GetFiles(ModsQuestDir, "VIGILANT*.rar", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (rar is null)
        {
            _output.WriteLine("Skipped: no VIGILANT V*.rar found under ModsForTests/QuestExpansiaon.");
            return;
        }

        var dest = Path.Combine(Path.GetTempPath(), "UW_Scan_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dest);
            var result = await new CompositeExtractor().ExtractAsync(rar, dest);
            Assert.True(result.ExtractedFiles.Count > 0, "VIGILANT extraction produced no files.");

            var plugin = ChooseMainPlugin(dest);
            if (plugin is null)
            {
                _output.WriteLine("Skipped: no .esp/.esm found inside the extracted VIGILANT layout.");
                return;
            }

            var rootPath = Path.GetDirectoryName(plugin)!;
            var source = new StoryModCatalogSource(rootPath, Path.GetFileName(plugin));
            var catalog = await new FolderCatalogScanner().ScanAsync(source);

            _output.WriteLine($"VIGILANT scan of '{Path.GetFileName(plugin)}': TotalArmo={catalog.Stats.TotalArmo}, GroupedSets={catalog.Stats.GroupedSets}, Warnings={catalog.Warnings.Count}");

            Assert.True(catalog.Stats.TotalArmo > 0, "Expected VIGILANT to contain armor records.");
            Assert.True(catalog.Stats.GroupedSets > 0, "Expected VIGILANT scan to produce at least one ArmorSet.");
        }
        finally
        {
            try
            {
                Directory.Delete(dest, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? ChooseMainPlugin(string root)
    {
        var candidates = Directory.EnumerateFiles(root, "*.esp", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.esm", SearchOption.AllDirectories))
            .ToList();

        var byName = candidates.FirstOrDefault(p => Path.GetFileName(p).Contains("vigilant", StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        return candidates.OrderByDescending(p => new FileInfo(p).Length).FirstOrDefault();
    }
}