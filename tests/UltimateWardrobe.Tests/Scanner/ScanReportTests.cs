using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class ScanReportTests
{
    private static ScanReport Build(
        IEnumerable<ScanWarning> warnings,
        IReadOnlyDictionary<SkipReason, int>? skippedByReason = null)
    {
        return ScanReportBuilder.Build(
            totalArmo: 100,
            totalArma: 88,
            groupedSetCount: 7,
            skippedByReason: skippedByReason ?? new Dictionary<SkipReason, int>(),
            outfitGroupedSetCount: 4,
            warnings: warnings,
            missingFiles: 3);
    }

    [Fact]
    public void Build_DedupesWarnings_ByMessageAndEditorId()
    {
        var report = Build(new[]
        {
            new ScanWarning("Duplicate", "IronCuirass"),
            new ScanWarning("Duplicate", "IronCuirass"),
            new ScanWarning("Duplicate", "OtherArmor"),
        });

        Assert.Equal(2, report.Warnings.Count);
    }

    [Fact]
    public void Build_SortsWarningsByMessageThenEditorId()
    {
        var report = Build(new[]
        {
            new ScanWarning("zzz", "A"),
            new ScanWarning("aaa", "B"),
            new ScanWarning("aaa", "A"),
            new ScanWarning("mmm", "A"),
        });

        Assert.Equal(
            new (string, string?)[] { ("aaa", "A"), ("aaa", "B"), ("mmm", "A"), ("zzz", "A") },
            report.Warnings.Select(w => (w.Message, w.EditorId)));
    }

    [Fact]
    public void Build_FillsStats()
    {
        var report = Build(
            Array.Empty<ScanWarning>(),
            new Dictionary<SkipReason, int>
            {
                [SkipReason.CreatureRace] = 2,
                [SkipReason.NoSlot] = 1,
                [SkipReason.Other] = 1,
            });

        Assert.Equal(100, report.Stats.TotalArmo);
        Assert.Equal(88, report.Stats.TotalArma);
        Assert.Equal(7, report.Stats.GroupedSets);
        Assert.Equal(4, report.Stats.Skipped);
        Assert.Equal(3, report.Stats.MissingFiles);
        Assert.Equal(4, report.OutfitGroupedSetCount);
        Assert.Equal(new[] { SkipReason.NoSlot, SkipReason.CreatureRace, SkipReason.Other }, report.Stats.SkippedByReason.Keys);
    }

    [Fact]
    public void BuildSummary_IsHumanReadable_AndDeterministic()
    {
        var report = Build(
            new[] { new ScanWarning("unresolvable race", "MysteryGauntlets") },
            new Dictionary<SkipReason, int>
            {
                [SkipReason.CreatureRace] = 2,
                [SkipReason.NoSlot] = 1,
            });

        var summary = report.BuildSummary();
        var again = report.BuildSummary();

        Assert.Equal(summary, again);

        Assert.Contains("Total armor records: 100", summary);
        Assert.Contains("Total armatures: 88", summary);
        Assert.Contains("Grouped sets: 7 (4 outfit-grouped)", summary);
        Assert.Contains("Skipped armor records: 3 (NoSlot: 1, CreatureRace: 2)", summary);
        Assert.Contains("Missing loose files: 3", summary);
        Assert.Contains("Warnings: 1", summary);
    }

    [Fact]
    public void BuildSummary_WithEmptyBreakdown_ListsOnlyTotalSkipped()
    {
        var report = Build(Array.Empty<ScanWarning>());

        var summary = report.BuildSummary();

        Assert.Contains("Skipped armor records: 0", summary);
        Assert.DoesNotContain("(", summary.Split('\n').First(l => l.Contains("Skipped armor records")));
    }

    [Fact]
    public void Guard_RoutesUnexpectedException_WithEditorId()
    {
        var ex = Assert.Throws<CatalogScanException>(() =>
            ScanReportBuilder.Guard("reading", "IronCuirass", () => throw new InvalidOperationException("boom")));

        Assert.Equal("IronCuirass", ex.EditorId);
        Assert.Contains("reading", ex.Message);
        Assert.Contains("IronCuirass", ex.Message);
    }

    [Fact]
    public void Guard_PassesCatalogScanExceptionThrough()
    {
        var original = new CatalogScanException("validation") { EditorId = "X" };

        var thrown = Assert.Throws<CatalogScanException>(() => ScanReportBuilder.Guard("step", "Other", () => throw original));

        Assert.Same(original, thrown);
        Assert.Equal("X", thrown.EditorId);
    }

    [Fact]
    public void Guard_PassesOperationCanceledExceptionThrough()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => ScanReportBuilder.Guard("step", "Armor", () => cts.Token.ThrowIfCancellationRequested()));
    }

    [Fact]
    public void Guard_ActionOverload_RoutesUnexpectedException()
    {
        var ex = Assert.Throws<CatalogScanException>(() => ScanReportBuilder.Guard("acting", "Helm", () => throw new FormatException("bad")));
        Assert.Equal("Helm", ex.EditorId);
    }
}