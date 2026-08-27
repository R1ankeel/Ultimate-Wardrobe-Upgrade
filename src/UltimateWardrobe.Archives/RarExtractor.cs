using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Archives.Native;

namespace UltimateWardrobe.Archives;

/// <summary>
/// P/Invoke extractor over runtimes/win-x64/native/UnRAR64.dll (native-first) with a SharpCompress fallback.
/// Handles rar; routes to the native engine when available and reports the engine in <see cref="ExtractResult.Engine"/>.
/// </summary>
public sealed class RarExtractor : IArchiveExtractor
{
    private readonly IRarNative _native;
    private readonly IArchiveExtractor _fallback;

    public RarExtractor(string? nativePath = null, IArchiveExtractor? fallback = null)
    {
        _native = new RarNative(nativePath);
        _fallback = fallback ?? new SharpCompressExtractor(ArchiveFormat.Rar);
    }

    public async Task<ExtractResult> ExtractAsync(string archivePath, string destDir, IProgress<ExtractProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path must not be empty.", nameof(archivePath));
        if (string.IsNullOrWhiteSpace(destDir)) throw new ArgumentException("Dest dir must not be empty.", nameof(destDir));
        if (!File.Exists(archivePath)) throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);

        var format = ArchiveFormatDetector.DetectFromFile(archivePath);
        if (format != ArchiveFormat.Rar)
        {
            throw new UnsupportedArchiveException($"RarExtractor cannot handle format {format} for {archivePath}");
        }

        if (_native is { IsAvailable: true })
        {
            try
            {
                var files = await Task.Run(() => _native.ExtractAll(archivePath, destDir, progress, cancellationToken), cancellationToken);
                return new ExtractResult(files, 0, format, NativeEngineNames.UnRar64Dll);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (NativeLibraryNotFoundException)
            {
                // Native disappeared after probe - fall through to managed fallback.
            }
            catch (ArchiveOpenException)
            {
                // Unsupported feature or corrupt archive reported by native - try managed fallback.
            }
        }

        return await _fallback.ExtractAsync(archivePath, destDir, progress, cancellationToken);
    }
}