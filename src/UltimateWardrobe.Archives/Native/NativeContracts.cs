using UltimateWardrobe.Core.Abstractions;

namespace UltimateWardrobe.Archives.Native;

/// <summary>
/// Contract for the native 7-Zip extraction engine (runtimes/win-x64/native/7z.dll).
/// Implementations expose P/Invoke over the 7-Zip CreateObject / IInArchive interface.
/// Each call must be fully synchronous. Swappable so tests can verify the extractor routing with fakes.
/// </summary>
public interface ISevenZipNative
{
    bool IsAvailable { get; }
    IReadOnlyList<string> ExtractAll(string archivePath, string destDir, IProgress<ExtractProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Contract for the native RAR extraction engine (runtimes/win-x64/native/UnRAR64.dll).
/// Implementations expose P/Invoke over the UnRAR DLL API (RAROpenArchiveEx / RARReadHeaderEx / RARProcessFileW).
/// Each call must be fully synchronous. Swappable so tests can verify the extractor routing with fakes.
/// </summary>
public interface IRarNative
{
    bool IsAvailable { get; }
    IReadOnlyList<string> ExtractAll(string archivePath, string destDir, IProgress<ExtractProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Human-readable engine names surfaced in <see cref="ExtractResult.Engine"/> for diagnostics and tests.
/// 7z.dll handles 7z and zip; UnRAR64.dll (RARLAB UnRAR) handles rar.
/// </summary>
public static class NativeEngineNames
{
    public const string SevenZipDll = "7z.dll";
    public const string UnRar64Dll = "UnRAR64.dll";
    public const string SharpCompress = "SharpCompress";
}