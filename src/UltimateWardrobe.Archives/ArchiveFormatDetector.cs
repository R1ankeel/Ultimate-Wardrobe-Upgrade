using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Archives;

public static class ArchiveFormatDetector
{
    private static readonly byte[] SevenZipMagic = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly byte[] RarMagic = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07];

    public static ArchiveFormat Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 6 && header[0] == SevenZipMagic[0] && header[1] == SevenZipMagic[1] && header[2] == SevenZipMagic[2] && header[3] == SevenZipMagic[3] && header[4] == SevenZipMagic[4] && header[5] == SevenZipMagic[5])
        {
            return ArchiveFormat.SevenZip;
        }

        if (header.Length >= 7 && header[0] == RarMagic[0] && header[1] == RarMagic[1] && header[2] == RarMagic[2] && header[3] == RarMagic[3] && header[4] == RarMagic[4] && header[5] == RarMagic[5])
        {
            // RAR covers both RAR4 and RAR5 - magic same prefix
            return ArchiveFormat.Rar;
        }

        if (header.Length >= 4)
        {
            // Zip: PK 03 04 (file), PK 05 06 (empty archive), PK 07 08 (spanned)
            if (header[0] == 0x50 && header[1] == 0x4B)
            {
                if ((header[2] == 0x03 && header[3] == 0x04) ||
                    (header[2] == 0x05 && header[3] == 0x06) ||
                    (header[2] == 0x07 && header[3] == 0x08))
                {
                    return ArchiveFormat.Zip;
                }
            }
        }

        return ArchiveFormat.Unknown;
    }

    public static ArchiveFormat DetectFromFile(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Path must not be empty.", nameof(archivePath));
        Span<byte> buffer = stackalloc byte[16];
        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        int read = fs.Read(buffer);
        return Detect(buffer[..read]);
    }

    public static async Task<ArchiveFormat> DetectFromFileAsync(string archivePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Path must not be empty.", nameof(archivePath));
        var buffer = new byte[16];
        await using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        int read = await fs.ReadAsync(buffer.AsMemory(0, 16), ct);
        return Detect(buffer.AsSpan(0, read));
    }
}
