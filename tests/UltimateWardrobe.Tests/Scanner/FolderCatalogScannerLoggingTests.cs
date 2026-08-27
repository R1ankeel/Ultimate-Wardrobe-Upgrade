using Microsoft.Extensions.Logging;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.Tests.Scanner;

public sealed record LogEntry(LogLevel Level, string Message);

/// <summary>
/// Captures every structured log event (all levels enabled, including Debug) so the 1.7.1
/// lifecycle events can be asserted deterministically.
/// </summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}

public sealed class FolderCatalogScannerLoggingTests
{
    [Fact]
    public async Task Scan_EmitsStructuredLifecycleEvents_InOrder()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var logger = new ListLogger<FolderCatalogScanner>();
        var scanner = new FolderCatalogScanner(logger);

        var catalog = await scanner.ScanAsync(new VanillaCatalogSource(dir.Root, new[] { SyntheticGroupingUniverse.FileName }));

        var startedIndex = logger.Entries.FindIndex(e => e.Level == LogLevel.Information && e.Message.Contains("started; source kind VanillaPlusDlc"));
        var pluginsLoadedIndex = logger.Entries.FindIndex(e => e.Level == LogLevel.Debug && e.Message.Contains("loaded (1/1)"));
        var recordsIndex = logger.Entries.FindIndex(e => e.Level == LogLevel.Information && e.Message.Contains("record index built for 1 plugins"));
        var groupedIndex = logger.Entries.FindIndex(e => e.Level == LogLevel.Information && e.Message.Contains("armors into 6 sets") && e.Message.Contains("outfit-grouped"));
        var finishedIndex = logger.Entries.FindIndex(e => e.Level == LogLevel.Information && e.Message.Contains("finished in") && e.Message.Contains("warnings"));

        Assert.True(startedIndex >= 0, "ScanStart event missing");
        Assert.True(pluginsLoadedIndex >= 0, "PluginLoaded event missing");
        Assert.True(recordsIndex >= 0, "RecordsFound event missing");
        Assert.True(groupedIndex >= 0, "Grouped event missing");
        Assert.True(finishedIndex >= 0, "finished event missing");

        Assert.True(startedIndex < recordsIndex, "ScanStart must precede RecordsFound");
        Assert.True(recordsIndex < groupedIndex, "RecordsFound must precede Grouped");
        Assert.True(groupedIndex < finishedIndex, "Grouped must precede finished");
        Assert.True(pluginsLoadedIndex < recordsIndex && pluginsLoadedIndex > startedIndex, "PluginLoaded must fire after ScanStart and before RecordsFound");
    }

    [Fact]
    public async Task StoryScan_MissingMaster_EmitsLogWarningPerMaster()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var logger = new ListLogger<FolderCatalogScanner>();
        var scanner = new FolderCatalogScanner(logger);
        var source = new StoryModCatalogSource(
            dir.Root,
            SyntheticGroupingUniverse.FileName,
            new[] { "MissingMaster.esm", "AlsoMissing.esm" });

        var catalog = await scanner.ScanAsync(source);

        var missingWarnings = logger.Entries
            .Where(e => e.Level == LogLevel.Warning && e.Message.Contains("missing master"))
            .ToList();

        Assert.Equal(2, missingWarnings.Count);
        Assert.Contains(missingWarnings, e => e.Message.Contains("MissingMaster.esm"));
        Assert.Contains(missingWarnings, e => e.Message.Contains("AlsoMissing.esm"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("finished in"));

        Assert.NotNull(catalog.Report);
    }

    [Fact]
    public async Task Scan_ExposesLastReport_IdenticalToCatalogReport()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var scanner = new FolderCatalogScanner();

        var catalog = await scanner.ScanAsync(new VanillaCatalogSource(dir.Root, new[] { SyntheticGroupingUniverse.FileName }));

        Assert.Same(catalog.Report, scanner.LastReport);
        Assert.NotNull(catalog.Report);
        Assert.Contains("Grouped sets: 6", catalog.Report!.BuildSummary());
    }
}