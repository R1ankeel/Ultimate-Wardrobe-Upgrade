using FluentAssertions;
using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.Archives;

[Trait("Category", "Unit")]
public class ArchiveFormatDetectorTests
{
    [Fact]
    public void Detect_SevenZip_Magic()
    {
        var header = new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x00 };
        ArchiveFormatDetector.Detect(header).Should().Be(ArchiveFormat.SevenZip);
    }

    [Fact]
    public void Detect_Rar_Magic()
    {
        var header = new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };
        ArchiveFormatDetector.Detect(header).Should().Be(ArchiveFormat.Rar);
    }

    [Theory]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04 })]
    [InlineData(new byte[] { 0x50, 0x4B, 0x05, 0x06 })]
    [InlineData(new byte[] { 0x50, 0x4B, 0x07, 0x08 })]
    public void Detect_Zip_Magic(byte[] header)
    {
        ArchiveFormatDetector.Detect(header).Should().Be(ArchiveFormat.Zip);
    }

    [Fact]
    public void Detect_Unknown_For_Empty()
    {
        ArchiveFormatDetector.Detect(Array.Empty<byte>()).Should().Be(ArchiveFormat.Unknown);
        ArchiveFormatDetector.Detect(new byte[] { 0x00, 0x01 }).Should().Be(ArchiveFormat.Unknown);
    }

    [Fact]
    public void Detect_Unknown_For_Random()
    {
        var header = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        ArchiveFormatDetector.Detect(header).Should().Be(ArchiveFormat.Unknown);
    }

    [Fact]
    public void Detect_Truncated_SevenZip_Returns_Unknown()
    {
        var header = new byte[] { 0x37, 0x7A, 0xBC }; // only 3 bytes
        ArchiveFormatDetector.Detect(header).Should().Be(ArchiveFormat.Unknown);
    }

    [Fact]
    public async Task DetectFromFile_Works()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"UW_Det_{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllBytesAsync(tmp, new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C });
            ArchiveFormatDetector.DetectFromFile(tmp).Should().Be(ArchiveFormat.SevenZip);
            (await ArchiveFormatDetector.DetectFromFileAsync(tmp)).Should().Be(ArchiveFormat.SevenZip);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void DetectFromFile_EmptyFile_Returns_Unknown()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"UW_Det_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(tmp, Array.Empty<byte>());
            ArchiveFormatDetector.DetectFromFile(tmp).Should().Be(ArchiveFormat.Unknown);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
