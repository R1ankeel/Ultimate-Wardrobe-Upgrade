using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Archives;

public sealed class CompositeExtractor : IArchiveExtractor
{
    private readonly SevenZipExtractor _sevenZip;
    private readonly RarExtractor _rar;
    private readonly int _maxDepth;
    private readonly long _maxTotalBytes;

    public CompositeExtractor(SevenZipExtractor? sevenZip = null, RarExtractor? rar = null, int maxDepth = 5, long maxTotalBytes = 10L * 1024 * 1024 * 1024)
    {
        _sevenZip = sevenZip ?? new SevenZipExtractor();
        _rar = rar ?? new RarExtractor();
        _maxDepth = maxDepth;
        _maxTotalBytes = maxTotalBytes;
    }

    public async Task<ExtractResult> ExtractAsync(string archivePath, string destDir, IProgress<ExtractProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path must not be empty.", nameof(archivePath));
        if (string.IsNullOrWhiteSpace(destDir)) throw new ArgumentException("Dest dir must not be empty.", nameof(destDir));

        var format = ArchiveFormatDetector.DetectFromFile(archivePath);
        if (format == ArchiveFormat.Unknown)
        {
            throw new UnsupportedArchiveException($"Unknown archive format for file: {archivePath}");
        }

        Directory.CreateDirectory(destDir);

        var allFiles = new List<string>();
        int nestedHandled = 0;
        long totalBytes = 0;

        // First extraction
        var first = await DispatchAsync(archivePath, destDir, progress, cancellationToken);
        allFiles.AddRange(first.ExtractedFiles);
        totalBytes += EstimateBytes(first.ExtractedFiles);
        if (totalBytes > _maxTotalBytes) throw new ArchiveTooLargeException($"Archive exceeds max total bytes {_maxTotalBytes}");

        // Recursive handling
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int depth = 0; depth < _maxDepth; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nested = Directory.EnumerateFiles(destDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (nested.Count == 0) break;

            bool any = false;
            foreach (var nestedPath in nested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Avoid re-processing same content via hash
                string hash;
                try { hash = ComputeQuickHash(nestedPath); }
                catch { hash = nestedPath; }
                if (!seenHashes.Add(hash)) continue;

                var nestedFormat = ArchiveFormatDetector.DetectFromFile(nestedPath);
                if (nestedFormat == ArchiveFormat.Unknown) continue;

                var res = await DispatchAsync(nestedPath, destDir, progress, cancellationToken);
                allFiles.AddRange(res.ExtractedFiles);
                totalBytes += EstimateBytes(res.ExtractedFiles);
                if (totalBytes > _maxTotalBytes) throw new ArchiveTooLargeException($"Archive exceeds max total bytes {_maxTotalBytes} at depth {depth}");

                // Delete nested archive file after successful extraction
                try { File.Delete(nestedPath); } catch { /* ignore */ }
                nestedHandled++;
                any = true;
            }

            if (!any) break;
        }

        // Exclude any deleted nested archive paths from allFiles? Keep only files that still exist.
        var existing = allFiles.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();

        return new ExtractResult(existing, nestedHandled, format, first.Engine);
    }

    private Task<ExtractResult> DispatchAsync(string archivePath, string destDir, IProgress<ExtractProgress>? progress, CancellationToken ct)
    {
        var format = ArchiveFormatDetector.DetectFromFile(archivePath);
        return format switch
        {
            ArchiveFormat.SevenZip => _sevenZip.ExtractAsync(archivePath, destDir, progress, ct),
            ArchiveFormat.Zip => _sevenZip.ExtractAsync(archivePath, destDir, progress, ct),
            ArchiveFormat.Rar => _rar.ExtractAsync(archivePath, destDir, progress, ct),
            _ => throw new UnsupportedArchiveException($"Unsupported format {format}")
        };
    }

    private static long EstimateBytes(IReadOnlyList<string> files)
    {
        long sum = 0;
        foreach (var f in files)
        {
            try { sum += new FileInfo(f).Length; } catch { }
        }
        return sum;
    }

    private static string ComputeQuickHash(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash);
    }
}
