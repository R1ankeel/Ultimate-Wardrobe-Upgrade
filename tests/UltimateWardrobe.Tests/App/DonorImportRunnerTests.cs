using FluentAssertions;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Tests.Persistence;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Sprint 6.3 - the real <see cref="DonorImportRunner"/> over a <see cref="DonorLibraryService"/> backed
/// by a stub extractor + stub classifier and real temp dirs (headless): the per-file progress order,
/// case-insensitive extension filtering, first-failure abort (earlier files committed, the failed file
/// appends nothing and its Source folder is cleaned up by Phase 2), and cancellation propagation. 0
/// failures / 0 warnings is the Sprint gate.
/// </summary>
[Trait("Category", "App")]
public class DonorImportRunnerTests : IDisposable
{
    private readonly string _root = TestHelpers.NewTempDir("UW_DonorRun_");
    private readonly StubExtractor _extractor = new();

    public void Dispose() => TestHelpers.DeleteDirectoryRetry(_root);

    [Fact]
    public async Task Import_filters_extensions_case_insensitively_and_reports_progress()
    {
        var library = new UltimateWardrobe.Core.Domain.DonorLibrary(Guid.NewGuid());
        var paths = CreateArchives("a.zip", "B.7Z", "c.rar", "readme.txt");
        var reports = new List<DonorImportProgress>();
        var progress = new Progress<DonorImportProgress>(reports.Add);

        var runner = BuildRunner();
        var imported = await runner.ImportAsync(paths, _root, library, null, progress);

        imported.Should().HaveCount(3);
        library.Assets.Should().HaveCount(3);
        reports.Should().Equal(
            new DonorImportProgress(1, 3),
            new DonorImportProgress(2, 3),
            new DonorImportProgress(3, 3));
    }

    [Fact]
    public async Task Import_supports_duplicate_paths_handled_once()
    {
        var library = new UltimateWardrobe.Core.Domain.DonorLibrary(Guid.NewGuid());
        var a = CreateFile("a.zip");
        var runner = BuildRunner();

        var imported = await runner.ImportAsync(new[] { a, a }, _root, library, null, null);

        imported.Should().ContainSingle();
        library.Assets.Should().ContainSingle();
    }

    [Fact]
    public async Task Import_no_supported_paths_returns_empty()
    {
        var library = new UltimateWardrobe.Core.Domain.DonorLibrary(Guid.NewGuid());
        var readme = CreateFile("readme.txt");
        var runner = BuildRunner();

        var imported = await runner.ImportAsync(new[] { readme }, _root, library, null, null);

        imported.Should().BeEmpty();
        library.Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_aborts_on_first_failure_commits_earlier_and_forgets_the_failed_file()
    {
        var library = new UltimateWardrobe.Core.Domain.DonorLibrary(Guid.NewGuid());
        var ok = CreateFile("ok.7z");
        var bad = CreateFile("bad.zip");
        _extractor.FailOn = Path.GetFileName(bad);

        var runner = BuildRunner();
        var act = async () => await runner.ImportAsync(new[] { ok, bad }, _root, library, null, null);
        await act.Should().ThrowAsync<InvalidOperationException>();

        library.Assets.Should().ContainSingle("the earlier file stays committed");
        var sourceRoot = Path.Combine(_root, "Source");
        if (Directory.Exists(sourceRoot))
        {
            Directory.GetDirectories(sourceRoot).Should()
                .HaveCount(library.Assets.Count, "only the committed file's Source/<id> folder remains; the failed one is cleaned up by Phase 2");
        }
    }

    [Fact]
    public async Task Import_propagates_cancellation_without_mutating_library()
    {
        var library = new UltimateWardrobe.Core.Domain.DonorLibrary(Guid.NewGuid());
        var a = CreateFile("a.zip");
        var runner = BuildRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await runner.ImportAsync(new[] { a }, _root, library, null, null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        library.Assets.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a.7z", true)]
    [InlineData("a.ZIP", true)]
    [InlineData("a.rar", true)]
    [InlineData("a.zip", true)]
    [InlineData("a.txt", false)]
    [InlineData("a.zip.bak", false)]
    [InlineData("", false)]
    public void IsSupportedArchive_detects_donor_extensions(string path, bool expected)
    {
        DonorImportRunner.IsSupportedArchive(path).Should().Be(expected);
    }

    private IReadOnlyList<string> CreateArchives(params string[] names)
    {
        var list = new List<string>();
        foreach (var name in names)
        {
            list.Add(CreateFile(name));
        }

        return list;
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes("archive-" + name));
        return path;
    }

    private DonorImportRunner BuildRunner()
    {
        var service = new UltimateWardrobe.DonorLibrary.DonorLibraryService(
            new DonorImportService(_extractor),
            new StubClassifier());
        return new DonorImportRunner(service);
    }

    private sealed class StubClassifier : IDonorClassifier
    {
        public Task<DonorAsset> ClassifyAsync(string extractedDir, Catalog? catalogHint = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new DonorAsset(
                Guid.NewGuid(), Path.GetFileName(extractedDir), extractedDir, DateTime.UtcNow, "class-hash",
                DonorAssetKind.FullReplacer));
    }

    private sealed class StubExtractor : IArchiveExtractor
    {
        public string? FailOn { get; set; }

        public Task<ExtractResult> ExtractAsync(string archivePath, string destDir, IProgress<ExtractProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (FailOn is not null && string.Equals(Path.GetFileName(archivePath), FailOn, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("extractor failed on a bad archive");
            }

            Directory.CreateDirectory(destDir);
            File.WriteAllText(Path.Combine(destDir, "body.nif"), "nif");
            return Task.FromResult(new ExtractResult(
                new[] { Path.Combine(destDir, "body.nif") }, 0, ArchiveFormat.Zip, "stub"));
        }
    }
}
