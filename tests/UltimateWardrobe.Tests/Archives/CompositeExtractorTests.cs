using FluentAssertions;
using UltimateWardrobe.Archives;

namespace UltimateWardrobe.Tests.Archives;

[Trait("Category", "Unit")]
public class CompositeExtractorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"UW_Comp_{Guid.NewGuid():N}");

    public CompositeExtractorTests() => Directory.CreateDirectory(_tempRoot);
    public void Dispose() { try { Directory.Delete(_tempRoot, true); } catch { } }

    [Fact]
    public async Task Extract_Zip_Dispatches_Via_SevenZip()
    {
        var zipPath = ArchiveTestHelper.CreateZipWithFiles(_tempRoot, new Dictionary<string, string> { ["a/b.txt"] = "hello", ["root.txt"] = "root" });
        var dest = Path.Combine(_tempRoot, "dest1");
        var sut = new CompositeExtractor();
        var res = await sut.ExtractAsync(zipPath, dest);
        File.Exists(Path.Combine(dest, "a", "b.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "root.txt")).Should().BeTrue();
        File.ReadAllText(Path.Combine(dest, "a", "b.txt")).Should().Be("hello");
        res.NestedHandled.Should().Be(0);
    }

    [Fact]
    public async Task Extract_Unknown_Throws()
    {
        var unknownPath = Path.Combine(_tempRoot, "unknown.bin");
        await File.WriteAllBytesAsync(unknownPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });
        var dest = Path.Combine(_tempRoot, "dest2");
        var sut = new CompositeExtractor();
        var act = async () => await sut.ExtractAsync(unknownPath, dest);
        await act.Should().ThrowAsync<UnsupportedArchiveException>();
    }

    [Fact]
    public async Task Extract_Nested_Zip_Recurses_And_Deletes_Inner()
    {
        var outer = ArchiveTestHelper.CreateNestedZip(_tempRoot);
        var dest = Path.Combine(_tempRoot, "dest3");
        var sut = new CompositeExtractor();
        var res = await sut.ExtractAsync(outer, dest);
        res.NestedHandled.Should().Be(1);
        File.Exists(Path.Combine(dest, "inner", "file.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "outer.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "inner.zip")).Should().BeFalse();
        File.ReadAllText(Path.Combine(dest, "inner", "file.txt")).Should().Be("inner-content");
    }

    [Fact]
    public async Task Extract_Traversal_Is_Skipped()
    {
        var zipPath = Path.Combine(_tempRoot, $"trav_{Guid.NewGuid():N}.zip");
        ArchiveTestHelper.CreateTraversalViaSharpCompress(zipPath);
        var dest = Path.Combine(_tempRoot, "dest4");
        var sut = new CompositeExtractor();
        var res = await sut.ExtractAsync(zipPath, dest);
        // good.txt should exist, evil.txt should NOT be outside dest nor inside via traversal
        File.Exists(Path.Combine(dest, "good.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "evil.txt")).Should().BeFalse();
        // Check no file escaped to parent
        var evilOutside = Path.GetFullPath(Path.Combine(dest, "..", "evil.txt"));
        File.Exists(evilOutside).Should().BeFalse();
    }

    [Fact]
    public async Task Extract_Cancellation_Throws()
    {
        var zipPath = ArchiveTestHelper.CreateZipWithFiles(_tempRoot, new Dictionary<string, string> { ["a.txt"] = "a" });
        var dest = Path.Combine(_tempRoot, "dest5");
        var sut = new CompositeExtractor();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await sut.ExtractAsync(zipPath, dest, null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MaxDepth_Respected()
    {
        // Create depth chain via multiple nested levels - but Composite maxDepth 5 will handle 1 level here
        var outer = ArchiveTestHelper.CreateNestedZip(_tempRoot);
        var dest = Path.Combine(_tempRoot, "dest6");
        var sut = new CompositeExtractor(maxDepth: 1);
        var res = await sut.ExtractAsync(outer, dest);
        res.NestedHandled.Should().Be(1);
    }
}
