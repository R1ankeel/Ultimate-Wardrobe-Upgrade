using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Core.Domain;

public sealed class DonorProvidedSet
{
    public string Id { get; init; }
    public string DisplayName { get; init; }

    public DonorProvidedSet(string id, string displayName)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("DisplayName must not be empty.", nameof(displayName));
        Id = id;
        DisplayName = displayName;
    }
}

public sealed class DonorAsset
{
    public Guid ImportId { get; init; }
    public string OriginalFileName { get; init; }
    public string ExtractedPath { get; init; }
    public DateTime ImportedAt { get; init; }
    public string ArchiveHash { get; init; }
    public DonorAssetKind Kind { get; init; }
    public IReadOnlyList<DonorProvidedSet> ProvidedSets { get; init; }
    public IReadOnlyList<string> FileManifest { get; init; }
    public IReadOnlyList<string> DetectedBodySlideFiles { get; init; }
    public IReadOnlyList<string> DetectedPhysicsFiles { get; init; }

    public DonorAsset(
        Guid importId,
        string originalFileName,
        string extractedPath,
        DateTime importedAt,
        string archiveHash,
        DonorAssetKind kind = DonorAssetKind.FullReplacer,
        IReadOnlyList<DonorProvidedSet>? providedSets = null,
        IReadOnlyList<string>? fileManifest = null,
        IReadOnlyList<string>? detectedBodySlideFiles = null,
        IReadOnlyList<string>? detectedPhysicsFiles = null)
    {
        if (importId == Guid.Empty) throw new ArgumentException("ImportId must not be empty.", nameof(importId));
        if (string.IsNullOrWhiteSpace(originalFileName)) throw new ArgumentException("OriginalFileName must not be empty.", nameof(originalFileName));
        if (string.IsNullOrWhiteSpace(extractedPath)) throw new ArgumentException("ExtractedPath must not be empty.", nameof(extractedPath));
        if (string.IsNullOrWhiteSpace(archiveHash)) throw new ArgumentException("ArchiveHash must not be empty.", nameof(archiveHash));

        ImportId = importId;
        OriginalFileName = originalFileName;
        ExtractedPath = extractedPath;
        ImportedAt = importedAt;
        ArchiveHash = archiveHash;
        Kind = kind;
        ProvidedSets = providedSets ?? Array.Empty<DonorProvidedSet>();
        FileManifest = fileManifest ?? Array.Empty<string>();
        DetectedBodySlideFiles = detectedBodySlideFiles ?? Array.Empty<string>();
        DetectedPhysicsFiles = detectedPhysicsFiles ?? Array.Empty<string>();
    }
}
