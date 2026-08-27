using SharpCompress.Archives;
using SharpCompress.Common;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Archives.Native;

namespace UltimateWardrobe.Archives;

/// <summary>
/// Managed extraction engine (SharpCompress) used as a fallback when the native 7z.dll / UnRAR64.dll
/// path is unavailable. Handles 7z, zip and rar and keeps the <see cref="IArchiveExtractor"/> contract swappable.
/// </summary>
internal sealed class SharpCompressExtractor : IArchiveExtractor
{
    private readonly ArchiveFormat[] _supported;

    public SharpCompressExtractor(params ArchiveFormat[] supported) => _supported = supported;

    public async Task<ExtractResult> ExtractAsync(string archivePath, string destDir, IProgress<ExtractProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path must not be empty.", nameof(archivePath));
        if (string.IsNullOrWhiteSpace(destDir)) throw new ArgumentException("Dest dir must not be empty.", nameof(destDir));
        if (!File.Exists(archivePath)) throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);

        var format = ArchiveFormatDetector.DetectFromFile(archivePath);
        if (!_supported.Contains(format))
        {
            throw new UnsupportedArchiveException($"SharpCompressExtractor cannot handle format {format} for {archivePath}");
        }

        Directory.CreateDirectory(destDir);
        var extracted = new List<string>();
        long bytesDone = 0;
        int filesDone = 0;

        await Task.Run(() =>
        {
            using var stream = File.OpenRead(archivePath);
            using var archive = ArchiveFactory.Open(stream);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsDirectory) continue;
                var key = entry.Key ?? string.Empty;
                if (!PathSanitizer.IsSafeEntry(key, out var sanitized))
                {
                    continue;
                }

                var full = PathSanitizer.GetSafeFullPath(destDir, sanitized);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                entry.WriteToFile(full, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });

                extracted.Add(full);
                filesDone++;
                bytesDone += entry.Size;
                progress?.Report(new ExtractProgress { FilesDone = filesDone, BytesDone = bytesDone });
            }
        }, cancellationToken);

        return new ExtractResult(extracted.AsReadOnly(), 0, format, NativeEngineNames.SharpCompress);
    }
}