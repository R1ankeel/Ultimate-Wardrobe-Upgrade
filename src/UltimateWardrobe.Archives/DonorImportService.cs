using System.Security.Cryptography;
using System.Text.Json;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Archives;

public sealed class DonorImportService
{
    private readonly IArchiveExtractor _extractor;

    public DonorImportService(IArchiveExtractor? extractor = null)
    {
        _extractor = extractor ?? new CompositeExtractor();
    }

    public async Task<DonorAsset> ImportAsync(string archivePath, string projectRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path must not be empty.", nameof(archivePath));
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("Project root must not be empty.", nameof(projectRoot));
        if (!File.Exists(archivePath)) throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);

        var importId = Guid.NewGuid();
        var dest = Path.Combine(projectRoot, "Source", importId.ToString());
        Directory.CreateDirectory(dest);

        string archiveHash;
        using (var sha = SHA256.Create())
        {
            await using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
            var hash = await sha.ComputeHashAsync(fs, cancellationToken);
            archiveHash = Convert.ToHexString(hash).ToLowerInvariant();
        }

        ArchiveFormat format = ArchiveFormat.Unknown;
        IReadOnlyList<DonorFileEntry> manifest = Array.Empty<DonorFileEntry>();
        int nestedHandled = 0;

        try
        {
            var result = await _extractor.ExtractAsync(archivePath, dest, null, cancellationToken);
            format = result.Format;
            nestedHandled = result.NestedHandled;

            // Build manifest relative to dest with per-file sizes (Sprint 2.0.2, Scope amendment #1)
            var files = Directory.EnumerateFiles(dest, "*.*", SearchOption.AllDirectories)
                .Select(f => new DonorFileEntry(Path.GetRelativePath(dest, f).Replace('\\', '/'), new FileInfo(f).Length))
                .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();
            manifest = files;

            // Write _meta.json
            var meta = new
            {
                importId = importId.ToString(),
                originalFileName = Path.GetFileName(archivePath),
                importedAtUtc = DateTime.UtcNow.ToString("O"),
                archiveHash,
                archiveFormat = format.ToString(),
                extractedFilesCount = manifest.Count,
                nestedHandled
            };
            var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            var metaPath = Path.Combine(dest, "_meta.json");
            await File.WriteAllTextAsync(metaPath, json, cancellationToken);

            var donor = new DonorAsset(
                importId,
                Path.GetFileName(archivePath),
                dest,
                DateTime.UtcNow,
                archiveHash,
                DonorAssetKind.FullReplacer,
                null,
                manifest,
                null,
                null);

            return donor;
        }
        catch
        {
            // Cleanup on failure
            try
            {
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
            }
            catch { }
            throw;
        }
    }
}
