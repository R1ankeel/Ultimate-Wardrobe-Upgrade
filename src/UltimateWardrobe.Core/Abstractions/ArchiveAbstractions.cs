using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Core.Abstractions;

public sealed class ExtractProgress
{
    public int FilesDone { get; init; }
    public long BytesDone { get; init; }
}

public sealed class ExtractResult
{
    public IReadOnlyList<string> ExtractedFiles { get; init; }
    public int NestedHandled { get; init; }
    public ArchiveFormat Format { get; init; }
    public string? Engine { get; init; }

    public ExtractResult(IReadOnlyList<string> extractedFiles, int nestedHandled, ArchiveFormat format, string? engine = null)
    {
        ExtractedFiles = extractedFiles ?? throw new ArgumentNullException(nameof(extractedFiles));
        NestedHandled = nestedHandled;
        Format = format;
        Engine = engine;
    }
}

public interface IArchiveExtractor
{
    Task<ExtractResult> ExtractAsync(string archivePath, string destDir, IProgress<ExtractProgress>? progress = null, CancellationToken cancellationToken = default);
}
