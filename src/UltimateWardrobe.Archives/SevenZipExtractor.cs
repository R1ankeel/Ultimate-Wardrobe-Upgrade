using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Archives.Native;

namespace UltimateWardrobe.Archives;

/// <summary>
/// P/Invoke extractor over runtimes/win-x64/native/7z.dll (native-first) with a SharpCompress fallback.
/// Handles 7z and zip; routes to the native engine when available and reports the engine in <see cref="ExtractResult.Engine"/>.
/// </summary>
public sealed class SevenZipExtractor : IArchiveExtractor
{
    private readonly ISevenZipNative? _native;
    private readonly IArchiveExtractor _fallback;

    public SevenZipExtractor(string? nativePath = null, IArchiveExtractor? fallback = null)
    {
        _native = new SevenZipNative(nativePath);
        _fallback = fallback ?? new SharpCompressExtractor(ArchiveFormat.SevenZip, ArchiveFormat.Zip);
    }

    public async Task<ExtractResult> ExtractAsync(string archivePath, string destDir, IProgress<ExtractProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path must not be empty.", nameof(archivePath));
        if (string.IsNullOrWhiteSpace(destDir)) throw new ArgumentException("Dest dir must not be empty.", nameof(destDir));
        if (!File.Exists(archivePath)) throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);

        var format = ArchiveFormatDetector.DetectFromFile(archivePath);
        if (format != ArchiveFormat.SevenZip && format != ArchiveFormat.Zip)
        {
            throw new UnsupportedArchiveException($"SevenZipExtractor cannot handle format {format} for {archivePath}");
        }

        if (_native is { IsAvailable: true })
        {
            try
            {
                var files = await Task.Run(() => _native.ExtractAll(archivePath, destDir, progress, cancellationToken), cancellationToken);
                return new ExtractResult(files, 0, format, NativeEngineNames.SevenZipDll);
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