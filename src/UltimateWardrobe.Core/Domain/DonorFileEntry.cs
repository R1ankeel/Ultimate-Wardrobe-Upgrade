namespace UltimateWardrobe.Core.Domain;

/// <summary>
/// One file extracted from a donor archive. Backs the <see cref="DonorAsset.FileManifest"/> -
/// relative path (slash-normalized) plus size in bytes - so the Phase 5 patcher can select and
/// report exact files (Sprint 2.0.2, Scope amendment #1).
/// </summary>
public sealed record DonorFileEntry
{
    public string RelativePath { get; init; }

    public long Length { get; init; }

    public DonorFileEntry()
    {
        RelativePath = string.Empty;
    }

    public DonorFileEntry(string relativePath, long length)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("RelativePath must not be empty.", nameof(relativePath));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must not be negative.");

        RelativePath = relativePath;
        Length = length;
    }
}