using FluentAssertions;
using UltimateWardrobe.Archives;
using UltimateWardrobe.Archives.Native;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.Archives;

/// <summary>
/// Tests that run the real native engines (7z.dll / UnRAR64.dll) against the committed golden archives
/// in tests/TestData/Archives and verify parity with the SharpCompress fallback.
/// </summary>
[Trait("Category", "Native")]
public class GoldenArchiveTests
{
    private static string ArchivesDir => Path.Combine(AppContext.BaseDirectory, "TestData", "Archives");

    private static string Golden(string name) => Path.Combine(ArchivesDir, name);

    private static Dictionary<string, string> ExpectedContents => new()
    {
        ["root.txt"] = "hello root",
        ["folder/sub.txt"] = "hello sub",
        ["folder/deep/nested.txt"] = "hello deep",
    };

    private static Dictionary<string, string> ReadExtracted(string dest)
    {
        return Directory.GetFiles(dest, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dest, f).Replace('\\', '/'), File.ReadAllText);
    }

    [Theory]
    [InlineData("sample.7z", NativeEngineNames.SevenZipDll)]
    [InlineData("sample.zip", NativeEngineNames.SevenZipDll)]
    public async Task Native_SevenZip_Extracts_Golden_Correctly(string goldenName, string expectedEngine)
    {
        var dest = Path.Combine(Path.GetTempPath(), $"UW_Golden_{Guid.NewGuid():N}");
        try
        {
            var sut = new SevenZipExtractor();
            var result = await sut.ExtractAsync(Golden(goldenName), dest);

            result.Engine.Should().Be(expectedEngine);
            ReadExtracted(dest).Should().BeEquivalentTo(ExpectedContents);
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }
    }

    [Fact]
    public async Task Native_Rar_Extracts_Golden_Correctly()
    {
        var dest = Path.Combine(Path.GetTempPath(), $"UW_Golden_{Guid.NewGuid():N}");
        try
        {
            var sut = new RarExtractor();
            var result = await sut.ExtractAsync(Golden("sample_rar5.rar"), dest);

            result.Engine.Should().Be(NativeEngineNames.UnRar64Dll);
            ReadExtracted(dest).Should().BeEquivalentTo(ExpectedContents);
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }
    }

    [Fact]
    public async Task Composite_Recurses_Into_Nested_Golden_Archive()
    {
        var dest = Path.Combine(Path.GetTempPath(), $"UW_Nested_{Guid.NewGuid():N}");
        try
        {
            var sut = new CompositeExtractor();
            var result = await sut.ExtractAsync(Golden("nested.7z"), dest);

            result.Engine.Should().Be(NativeEngineNames.SevenZipDll);
            result.NestedHandled.Should().BeGreaterThanOrEqualTo(1);
            ReadExtracted(dest).Should().ContainKey("outer.txt").WhoseValue.Should().Be("outer-content");
            ReadExtracted(dest).Should().ContainKey("inner.txt").WhoseValue.Should().Be("inner-content");
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }
    }

    [Theory]
    [InlineData("sample.7z")]
    [InlineData("sample.zip")]
    [InlineData("sample_rar5.rar")]
    public async Task Native_And_SharpCompress_Produce_Identical_Output(string goldenName)
    {
        var nativeDest = Path.Combine(Path.GetTempPath(), $"UW_ParityNative_{Guid.NewGuid():N}");
        var fallbackDest = Path.Combine(Path.GetTempPath(), $"UW_ParityFallback_{Guid.NewGuid():N}");
        try
        {
            var path = Golden(goldenName);
            Directory.CreateDirectory(nativeDest);
            Directory.CreateDirectory(fallbackDest);

            var isRar = path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);
            var fallback = new SharpCompressExtractor(isRar ? ArchiveFormat.Rar : ArchiveFormat.SevenZip, ArchiveFormat.Zip);
            var engine = isRar ? (IArchiveExtractor)new RarExtractor() : new SevenZipExtractor();

            var nativeResult = await engine.ExtractAsync(path, nativeDest);
            var fallbackResult = await fallback.ExtractAsync(path, fallbackDest);

            nativeResult.Engine.Should().NotBe(NativeEngineNames.SharpCompress);
            fallbackResult.Engine.Should().Be(NativeEngineNames.SharpCompress);
            ReadExtracted(nativeDest).Should().BeEquivalentTo(ReadExtracted(fallbackDest));
        }
        finally
        {
            try { Directory.Delete(nativeDest, true); } catch { }
            try { Directory.Delete(fallbackDest, true); } catch { }
        }
    }

    [Theory]
    [InlineData("sample.7z")]
    [InlineData("sample.zip")]
    [InlineData("sample_rar5.rar")]
    public async Task Missing_Native_Dll_Falls_Back_To_SharpCompress(string goldenName)
    {
        var dest = Path.Combine(Path.GetTempPath(), $"UW_Fallback_{Guid.NewGuid():N}");
        try
        {
            var path = Golden(goldenName);
            IArchiveExtractor sut = path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)
                ? (IArchiveExtractor)new RarExtractor(nativePath: Path.Combine(Path.GetTempPath(), "missing", "UnRAR64.dll"))
                : new SevenZipExtractor(nativePath: Path.Combine(Path.GetTempPath(), "missing", "7z.dll"));

            var result = await sut.ExtractAsync(path, dest);

            result.Engine.Should().Be(NativeEngineNames.SharpCompress);
            ReadExtracted(dest).Should().BeEquivalentTo(ExpectedContents);
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }
    }

    [Theory]
    [InlineData("sample.7z")]
    [InlineData("sample.zip")]
    [InlineData("sample_rar5.rar")]
    public async Task PreCanceled_Token_Aborts_Extraction(string goldenName)
    {
        var dest = Path.Combine(Path.GetTempPath(), $"UW_Cancel_{Guid.NewGuid():N}");
        try
        {
            var path = Golden(goldenName);
            IArchiveExtractor sut = path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)
                ? new RarExtractor()
                : new SevenZipExtractor();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await sut.Invoking(s => s.ExtractAsync(path, dest, cancellationToken: cts.Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }
    }
}