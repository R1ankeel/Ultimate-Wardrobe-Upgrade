using System.Runtime.InteropServices;
using System.Text;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Archives.Native;

/// <summary>
/// P/Invoke wrapper over runtimes/win-x64/native/UnRAR64.dll (RARLAB UnRAR).
/// Uses the UnRAR DLL API: RAROpenArchiveEx / RARReadHeaderEx / RARProcessFileW / RARCloseArchive.
/// Struct layouts match the official unrar dll.hpp (packed, char* first) as verified against UnRAR 7.x.
/// The RARHeaderDataEx buffer must be zeroed before each RARReadHeaderEx call, because UnRAR reads input fields
/// (ArcNameEx / FileNameEx / RedirName / RedirNameSize) that live in what older docs called the Reserved area.
/// Extracts .rar (RAR4 and RAR5) archives with entry-name sanitization, per-file progress and cancellation.
/// </summary>
public sealed class RarNative : IRarNative
{
    private static readonly string DefaultNativePath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "UnRAR64.dll");

    private const int RarOmExtract = 1;
    private const int RarSkip = 0;
    private const int RarExtract = 2;
    private const int ErarEndArchive = 10;
    private const uint RhdfDirectory = 0x20;

    // Offsets inside the packed RAROpenArchiveDataEx (verified: Marshal.SizeOf == 176).
    private const int OpenArcName = 0;
    private const int OpenArcNameW = 8;
    private const int OpenOpenMode = 16;
    private const int OpenOpenResult = 20;

    // Offsets inside the packed RARHeaderDataEx (verified: Marshal.SizeOf == 10244).
    private const int HeaderFileNameW = 4096;
    private const int HeaderFlags = 6144;
    private const int HeaderUnpSize = 6156;
    private const int HeaderUnpSizeHigh = 6160;

    private static readonly int OpenDataSize;
    private static readonly int HeaderDataSize;

    static RarNative()
    {
        OpenDataSize = Marshal.SizeOf<RarOpenArchiveDataEx>();
        HeaderDataSize = Marshal.SizeOf<RarHeaderDataEx>();
        if (OpenDataSize != 176) throw new InvalidOperationException($"RAROpenArchiveDataEx layout changed (got {OpenDataSize} bytes, expected 176).");
        if (HeaderDataSize != 10244) throw new InvalidOperationException($"RARHeaderDataEx layout changed (got {HeaderDataSize} bytes, expected 10244).");
    }

    private readonly string _nativePath;

    public string NativePath => _nativePath;
    public bool IsAvailable { get; }

    public RarNative(string? nativePath = null)
    {
        _nativePath = nativePath ?? DefaultNativePath;
        IsAvailable = Probe();
    }

    private bool Probe()
    {
        try
        {
            if (!File.Exists(_nativePath)) return false;
            if (!NativeLibrary.TryLoad(_nativePath, out var handle)) return false;
            var ok = NativeLibrary.GetExport(handle, "RAROpenArchiveEx") != IntPtr.Zero;
            NativeLibrary.Free(handle);
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<string> ExtractAll(string archivePath, string destDir, IProgress<ExtractProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new NativeLibraryNotFoundException(
                $"UnRAR64.dll not found or loadable at {_nativePath}. Native RAR extraction requires UnRAR64.dll next to the app (runtimes/win-x64/native).");
        }

        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path must not be empty.", nameof(archivePath));
        if (string.IsNullOrWhiteSpace(destDir)) throw new ArgumentException("Dest dir must not be empty.", nameof(destDir));
        if (!File.Exists(archivePath)) throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destDir);

        var format = ArchiveFormatDetector.DetectFromFile(archivePath);
        if (format != ArchiveFormat.Rar)
        {
            throw new UnsupportedArchiveException($"RarNative cannot handle format {format} for {archivePath}");
        }

        IntPtr lib;
        try
        {
            lib = NativeLibrary.Load(_nativePath);
        }
        catch (Exception ex)
        {
            throw new NativeLibraryNotFoundException($"UnRAR64.dll could not be loaded from {_nativePath}: {ex.Message}");
        }

        try
        {
            var open = NativeHelper.GetExport<RarOpenArchiveExFn>(lib, "RAROpenArchiveEx");
            var readHeader = NativeHelper.GetExport<RarReadHeaderExFn>(lib, "RARReadHeaderEx");
            var processFile = NativeHelper.GetExport<RarProcessFileWFn>(lib, "RARProcessFileW");
            var close = NativeHelper.GetExport<RarCloseArchiveFn>(lib, "RARCloseArchive");

            var handle = OpenArchive(open, archivePath, out var openResult);
            if (handle == IntPtr.Zero || openResult != 0)
            {
                throw new ArchiveOpenException($"RAROpenArchiveEx failed with code {openResult} for {archivePath}");
            }

            try
            {
                return ExtractLoop(handle, readHeader, processFile, destDir, progress, cancellationToken);
            }
            finally
            {
                close(handle);
            }
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    private unsafe IReadOnlyList<string> ExtractLoop(
        nint handle,
        RarReadHeaderExFn readHeader,
        RarProcessFileWFn processFile,
        string destDir,
        IProgress<ExtractProgress>? progress,
        CancellationToken cancellationToken)
    {
        var header = (byte*)Marshal.AllocHGlobal(HeaderDataSize);
        var destW = (byte*)Marshal.StringToHGlobalUni(destDir);
        var extracted = new List<string>();
        int filesDone = 0;
        long bytesDone = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                new Span<byte>(header, HeaderDataSize).Clear();

                var rc = readHeader(handle, header);
                if (rc != 0)
                {
                    if (rc == ErarEndArchive) break;
                    throw new ArchiveOpenException($"RARReadHeaderEx failed with code {rc} for the archive under {destDir}");
                }

                bool isDir = (ReadUInt(header + HeaderFlags) & RhdfDirectory) != 0;
                if (isDir || !PathSanitizer.IsSafeEntry(ReadWide(header + HeaderFileNameW, 1024), out var rel))
                {
                    rc = processFile(handle, RarSkip, null, null);
                    if (rc != 0 && rc != ErarEndArchive)
                    {
                        throw new ArchiveOpenException($"RARProcessFileW (skip) failed with code {rc}");
                    }
                    continue;
                }

                ulong size = ReadUInt(header + HeaderUnpSize) | ((ulong)ReadUInt(header + HeaderUnpSizeHigh) << 32);
                var fullPath = PathSanitizer.GetSafeFullPath(destDir, rel);

                rc = processFile(handle, RarExtract, destW, null);
                if (rc != 0 && rc != ErarEndArchive)
                {
                    throw new ArchiveOpenException($"RARProcessFileW failed with code {rc} for entry '{rel}'");
                }

                extracted.Add(fullPath);
                filesDone++;
                bytesDone += (long)size;
                progress?.Report(new ExtractProgress { FilesDone = filesDone, BytesDone = bytesDone });
            }
        }
        finally
        {
            Marshal.FreeHGlobal((nint)header);
            Marshal.FreeHGlobal((nint)destW);
        }

        return extracted.AsReadOnly();
    }

    private unsafe nint OpenArchive(RarOpenArchiveExFn open, string archivePath, out int openResult)
    {
        var full = Path.GetFullPath(archivePath);
        byte[] ansiBytes = Encoding.ASCII.GetBytes(full + "\0");
        byte[] wideBytes = Encoding.Unicode.GetBytes(full + "\0");

        var ansi = (byte*)Marshal.AllocHGlobal(ansiBytes.Length);
        var wide = (byte*)Marshal.AllocHGlobal(wideBytes.Length);
        try
        {
            fixed (byte* bAnsi = ansiBytes) Buffer.MemoryCopy(bAnsi, ansi, ansiBytes.Length, ansiBytes.Length);
            fixed (byte* bWide = wideBytes) Buffer.MemoryCopy(bWide, wide, wideBytes.Length, wideBytes.Length);

            var data = (byte*)Marshal.AllocHGlobal(OpenDataSize);
            try
            {
                new Span<byte>(data, OpenDataSize).Clear();
                *(nint*)(data + OpenArcName) = (nint)ansi;
                *(nint*)(data + OpenArcNameW) = (nint)wide;
                *(uint*)(data + OpenOpenMode) = RarOmExtract;
                *(uint*)(data + OpenOpenResult) = 0;

                var handle = open(data);
                openResult = (int)*(uint*)(data + OpenOpenResult);
                return handle;
            }
            finally
            {
                Marshal.FreeHGlobal((nint)data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal((nint)ansi);
            Marshal.FreeHGlobal((nint)wide);
        }
    }

    private static unsafe uint ReadUInt(byte* p) => *(uint*)p;

    private static unsafe string ReadWide(byte* p, int maxChars)
    {
        var span = new ReadOnlySpan<byte>(p, maxChars * 2);
        var s = Encoding.Unicode.GetString(span);
        int nul = s.IndexOf('\0');
        return nul >= 0 ? s[..nul] : s;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate nint RarOpenArchiveExFn(byte* data);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int RarReadHeaderExFn(nint handle, byte* header);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int RarProcessFileWFn(nint handle, int operation, byte* destPath, byte* destName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int RarCloseArchiveFn(nint handle);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private unsafe struct RarOpenArchiveDataEx
    {
        public nint ArcName;
        public nint ArcNameW;
        public uint OpenMode;
        public uint OpenResult;
        public nint CmtBuf;
        public uint CmtBufSize;
        public uint CmtSize;
        public uint CmtState;
        public uint Flags;
        public nint Callback;
        public nint UserData;
        public uint OpFlags;
        public nint CmtBufW;
        public nint MarkOfTheWeb;
        public fixed uint Reserved[23];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private unsafe struct RarHeaderDataEx
    {
        public fixed byte ArcName[1024];
        public fixed byte ArcNameW[2048];
        public fixed byte FileName[1024];
        public fixed byte FileNameW[2048];
        public uint Flags;
        public uint PackSize;
        public uint PackSizeHigh;
        public uint UnpSize;
        public uint UnpSizeHigh;
        public uint HostOS;
        public uint FileCRC;
        public uint FileTime;
        public uint UnpVer;
        public uint Method;
        public uint FileAttr;
        public nint CmtBuf;
        public uint CmtBufSize;
        public uint CmtSize;
        public uint CmtState;
        public uint DictSize;
        public uint HashType;
        public fixed byte Hash[32];
        public uint RedirType;
        public nint RedirName;
        public uint RedirNameSize;
        public uint DirTarget;
        public uint MtimeLow;
        public uint MtimeHigh;
        public uint CtimeLow;
        public uint CtimeHigh;
        public uint AtimeLow;
        public uint AtimeHigh;
        public nint ArcNameEx;
        public uint ArcNameExSize;
        public nint FileNameEx;
        public uint FileNameExSize;
        public fixed uint Reserved[982];
    }
}