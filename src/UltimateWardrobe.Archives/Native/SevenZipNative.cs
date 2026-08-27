using System.Runtime.InteropServices;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Archives.Native;

/// <summary>
/// P/Invoke wrapper over runtimes/win-x64/native/7z.dll using the 7-Zip export interface:
/// CreateObject(CLSID_CFormat*, IID_IInArchive) + COM-style IInArchive / IInStream / IArchiveExtractCallback.
/// Extracts .7z and .zip with entry-name sanitization, per-file progress and cancellation.
/// RAR archives are handled by UnRAR64.dll (see <see cref="RarNative"/>).
/// </summary>
public sealed class SevenZipNative : ISevenZipNative
{
    private static readonly Guid Clsid7z = new("23170F69-40C1-278A-1000-000110070000");
    private static readonly Guid ClsidZip = new("23170F69-40C1-278A-1000-000110010000");
    private static readonly Guid IidIInArchive = new("23170F69-40C1-278A-0000-000600600000");

    private static readonly Guid IidIUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IidIInStream = new("23170F69-40C1-278A-0000-000600030000");
    private static readonly Guid IidIOutStream = new("23170F69-40C1-278A-0000-000600050000");
    private static readonly Guid IidArchiveExtractCallback = new("23170F69-40C1-278A-0000-000600300000");

    private const uint PropIdPath = 3;
    private const uint PropIdIsDir = 6;
    private const uint PropIdSize = 7;

    private const ushort VtBool = 0x000B;
    private const ushort VtUi8 = 0x0015;

    private readonly string _nativePath;

    public string NativePath => _nativePath;
    public bool IsAvailable { get; }

    public SevenZipNative(string? nativePath = null)
    {
        _nativePath = nativePath ?? Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "7z.dll");
        IsAvailable = Probe();
    }

    private bool Probe()
    {
        try
        {
            if (!File.Exists(_nativePath)) return false;
            if (!NativeLibrary.TryLoad(_nativePath, out var handle)) return false;
            var ok = NativeLibrary.GetExport(handle, "CreateObject") != IntPtr.Zero;
            NativeLibrary.Free(handle);
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public unsafe IReadOnlyList<string> ExtractAll(string archivePath, string destDir, IProgress<ExtractProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new NativeLibraryNotFoundException(
                $"7z.dll not found or loadable at {_nativePath}. Native extraction requires 7z.dll next to the app (runtimes/win-x64/native).");
        }

        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path must not be empty.", nameof(archivePath));
        if (string.IsNullOrWhiteSpace(destDir)) throw new ArgumentException("Dest dir must not be empty.", nameof(destDir));
        if (!File.Exists(archivePath)) throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destDir);

        var format = ArchiveFormatDetector.DetectFromFile(archivePath);
        if (format != ArchiveFormat.SevenZip && format != ArchiveFormat.Zip)
        {
            throw new UnsupportedArchiveException($"SevenZipNative cannot handle format {format} for {archivePath}");
        }

        IntPtr lib;
        try
        {
            lib = NativeLibrary.Load(_nativePath);
        }
        catch (Exception ex)
        {
            throw new NativeLibraryNotFoundException($"7z.dll could not be loaded from {_nativePath}: {ex.Message}");
        }

        try
        {
            var createObject = NativeHelper.GetExport<NativeDelegates.CreateObjectFn>(lib, "CreateObject");
            var candidates = format switch
            {
                ArchiveFormat.SevenZip => new[] { Clsid7z },
                ArchiveFormat.Zip => new[] { ClsidZip },
                _ => Array.Empty<Guid>(),
            };

            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
            using var inStream = new ComInStream(stream);

            ArchiveOpenException? openError = null;
            IInArchiveProxy? archive = null;
            int hr;
            foreach (var candidate in candidates)
            {
                var clsid = candidate;
                var iid = IidIInArchive;
                hr = createObject(ref clsid, ref iid, out var archivePtr);
                if (hr != NativeHresult.S_OK || archivePtr == IntPtr.Zero) continue;

                archive = new IInArchiveProxy(archivePtr);
                hr = archive.Open(inStream.Self, IntPtr.Zero, IntPtr.Zero);
                if (hr == NativeHresult.S_OK) break;

                openError = new ArchiveOpenException($"IInArchive::Open failed with HRESULT 0x{hr:X8} for {archivePath} (clsid {candidate})");
            }

            if (archive is null)
            {
                throw openError ?? new ArchiveOpenException($"7z.dll CreateObject failed for {archivePath} (no suitable handler)");
            }

            ExtractContext? ctx = null;
            try
            {
                using (archive)
                {
                    hr = archive.GetNumberOfItems(out var count);
                    if (hr != NativeHresult.S_OK)
                    {
                        throw new ArchiveOpenException($"IInArchive::GetNumberOfItems failed with HRESULT 0x{hr:X8}");
                    }

                    ctx = ExtractContext.Plan(count, archive, destDir, cancellationToken);
                    using var callback = new ExtractCallbackProxy(ctx, progress);

                    // Extract requires an explicit indices array; NULL indices with numItems > 0 crashes this 7z.dll.
                    var indices = new uint[count];
                    for (uint i = 0; i < count; i++) indices[i] = i;
                    fixed (uint* p = indices)
                    {
                        hr = archive.Extract((IntPtr)p, count, 0, callback.Self);
                    }
                }

                if (hr != NativeHresult.S_OK && cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                if (hr < 0)
                {
                    throw new ArchiveOpenException($"IInArchive::Extract failed with HRESULT 0x{hr:X8} for {archivePath}");
                }

                return ctx!.Extracted;
            }
            finally
            {
                ctx?.Dispose();
            }
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    private sealed class ExtractContext : IDisposable
    {
        private readonly CancellationToken _ct;
        private readonly string _destDir;
        private readonly List<string> _extracted = new();
        private readonly List<EntryPlan> _entries = new();
        private readonly IInArchiveProxy _archive;
        private int _filesDone;
        private long _bytesDone;

        public IReadOnlyList<string> Extracted => _extracted;
        public bool IsCancellationRequested => _ct.IsCancellationRequested;

        private ExtractContext(IInArchiveProxy archive, string destDir, CancellationToken ct)
        {
            _archive = archive;
            _destDir = destDir;
            _ct = ct;
        }

        public static ExtractContext Plan(uint count, IInArchiveProxy archive, string destDir, CancellationToken ct)
        {
            var ctx = new ExtractContext(archive, destDir, ct);
            ctx.BuildPlan(count);
            return ctx;
        }

        private unsafe void BuildPlan(uint count)
        {
            var pv = stackalloc byte[16];
            for (uint i = 0; i < count; i++)
            {
                _ct.ThrowIfCancellationRequested();
                string path = ReadPath(i, (nint)pv);
                bool isDir = ReadIsDir(i, (nint)pv);
                ulong size = ReadSize(i, (nint)pv);

                string? safe = null;
                if (!isDir && !string.IsNullOrEmpty(path) && PathSanitizer.IsSafeEntry(path, out var rel))
                {
                    safe = rel;
                }

                if (safe is null)
                {
                    _entries.Add(new EntryPlan(i, null, null, 0));
                    continue;
                }

                var full = PathSanitizer.GetSafeFullPath(_destDir, safe);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                _entries.Add(new EntryPlan(i, safe, full, size));
            }
        }

        private string ReadPath(uint index, nint pv)
        {
            if (_archive.GetProperty(index, PropIdPath, pv) != NativeHresult.S_OK) return string.Empty;
            return NativeHelper.ReadPropVariantString(pv) ?? string.Empty;
        }

        private unsafe bool ReadIsDir(uint index, nint pv)
        {
            if (_archive.GetProperty(index, PropIdIsDir, pv) != NativeHresult.S_OK) return false;
            ushort vt = *(ushort*)pv;
            return vt == VtBool && *(short*)(pv + 8) != 0;
        }

        private unsafe ulong ReadSize(uint index, nint pv)
        {
            if (_archive.GetProperty(index, PropIdSize, pv) != NativeHresult.S_OK) return 0;
            ushort vt = *(ushort*)pv;
            return vt == VtUi8 ? *(ulong*)(pv + 8) : 0;
        }

        public string? TryGetFullPath(uint index)
        {
            return _entries[(int)index].FullPath;
        }

        public void OnFileExtracted(uint index)
        {
            var plan = _entries[(int)index];
            if (plan.FullPath is null) return;
            _extracted.Add(plan.FullPath);
            _filesDone++;
            _bytesDone += (long)plan.Size;
        }

        public (int FilesDone, long BytesDone) Counters => (_filesDone, _bytesDone);

        public void CheckCancel() => _ct.ThrowIfCancellationRequested();

        public void Dispose()
        {
        }
    }

    private sealed class EntryPlan
    {
        public uint Index { get; }
        public string? SafeRelative { get; }
        public string? FullPath { get; }
        public ulong Size { get; }

        public EntryPlan(uint index, string? safeRelative, string? fullPath, ulong size)
        {
            Index = index;
            SafeRelative = safeRelative;
            FullPath = fullPath;
            Size = size;
        }
    }

    private sealed unsafe class IInArchiveProxy : IDisposable
    {
        private readonly IntPtr _obj;
        private int _released;

        public IInArchiveProxy(IntPtr obj) => _obj = obj;

        public int Open(IntPtr stream, IntPtr maxCheckStartPosition, IntPtr openCallback)
        {
            var fn = NativeHelper.VtblFn<NativeDelegates.IInArchiveOpenFn>(_obj, 3);
            return fn(_obj, stream, maxCheckStartPosition, openCallback);
        }

        public int Close()
        {
            var fn = NativeHelper.VtblFn<NativeDelegates.IInArchiveCloseFn>(_obj, 4);
            return fn(_obj);
        }

        public int GetNumberOfItems(out uint count)
        {
            fixed (uint* p = &count)
            {
                var fn = NativeHelper.VtblFn<NativeDelegates.IInArchiveGetCountFn>(_obj, 5);
                return fn(_obj, p);
            }
        }

        public int GetProperty(uint index, uint propId, nint value)
        {
            var fn = NativeHelper.VtblFn<NativeDelegates.IInArchiveGetPropertyFn>(_obj, 6);
            return fn(_obj, index, propId, value);
        }

        public int Extract(IntPtr indices, uint numItems, int testMode, IntPtr callback)
        {
            var fn = NativeHelper.VtblFn<NativeDelegates.IInArchiveExtractFn>(_obj, 7);
            return fn(_obj, indices, numItems, testMode, callback);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                try
                {
                    var fn = NativeHelper.VtblFn<NativeDelegates.IUnknownReleaseFn>(_obj, 2);
                    fn(_obj);
                }
                catch
                {
                }
            }
        }
    }

    private unsafe sealed class ComInStream : NativeComObject
    {
        private static readonly IntPtr Vtbl;
        private readonly Stream _stream;

        static ComInStream()
        {
            var methods = new Delegate[]
            {
                new NativeDelegates.NativeQueryInterfaceFn(InStreamQueryInterface),
                new NativeDelegates.NativeAddRefFn(InStreamAddRef),
                new NativeDelegates.NativeReleaseFn(InStreamRelease),
                new NativeDelegates.NativeReadFn(InStreamRead),
                new NativeDelegates.NativeSeekFn(InStreamSeek),
            };
            KeepAlive.Add(methods);
            Vtbl = BuildVtbl(5, methods);
        }

        public ComInStream(Stream stream) : base(Vtbl, null)
        {
            _stream = stream;
            SetInstance(this);
        }

        private Stream Stream => _stream;

        private static int InStreamQueryInterface(nint self, ref Guid iid, out nint ppv)
        {
            if (iid == IidIUnknown || iid == IidIInStream)
            {
                ppv = self;
                return NativeHresult.S_OK;
            }
            ppv = IntPtr.Zero;
            return NativeHresult.E_NOINTERFACE;
        }

        private static ComInStream Get(nint self) => (ComInStream)Instance(self);

        private static int InStreamAddRef(nint self) => Get(self).AddRef();

        private static int InStreamRelease(nint self) => Get(self).Release();

        private static int InStreamRead(nint self, nint data, uint size, nint processed)
        {
            var stream = Get(self).Stream;
            int read = stream.Read(new Span<byte>((byte*)data, (int)size));
            if (processed != IntPtr.Zero) *(uint*)processed = (uint)read;
            return NativeHresult.S_OK;
        }

        private static int InStreamSeek(nint self, long offset, uint origin, nint newPos)
        {
            var stream = Get(self).Stream;
            long result = origin switch
            {
                0 => stream.Seek(offset, SeekOrigin.Begin),
                1 => stream.Seek(offset, SeekOrigin.Current),
                2 => stream.Seek(offset, SeekOrigin.End),
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (newPos != IntPtr.Zero) *(ulong*)newPos = (ulong)result;
            return NativeHresult.S_OK;
        }

        protected override void DisposeManaged() => _stream.Dispose();
    }

    private unsafe sealed class ManagedOutStream : NativeComObject
    {
        private static readonly IntPtr Vtbl;
        private readonly Stream _stream;
        private readonly ExtractContext _ctx;

        static ManagedOutStream()
        {
            var methods = new Delegate[]
            {
                new NativeDelegates.NativeQueryInterfaceFn(OutStreamQueryInterface),
                new NativeDelegates.NativeAddRefFn(OutStreamAddRef),
                new NativeDelegates.NativeReleaseFn(OutStreamRelease),
                new NativeDelegates.NativeWriteFn(OutStreamWrite),
                new NativeDelegates.NativeFlushFn(OutStreamFlush),
            };
            KeepAlive.Add(methods);
            Vtbl = BuildVtbl(5, methods);
        }

        public ManagedOutStream(Stream stream, ExtractContext ctx) : base(Vtbl, null)
        {
            _stream = stream;
            _ctx = ctx;
            SetInstance(this);
        }

        private static int OutStreamQueryInterface(nint self, ref Guid iid, out nint ppv)
        {
            if (iid == IidIUnknown || iid == IidIOutStream)
            {
                ppv = self;
                return NativeHresult.S_OK;
            }
            ppv = IntPtr.Zero;
            return NativeHresult.E_NOINTERFACE;
        }

        private static ManagedOutStream Get(nint self) => (ManagedOutStream)Instance(self);

        private static int OutStreamAddRef(nint self) => Get(self).AddRef();

        private static int OutStreamRelease(nint self) => Get(self).Release();

        private static int OutStreamWrite(nint self, nint data, uint size, nint processed)
        {
            var s = Get(self);
            s._ctx.CheckCancel();
            s._stream.Write(new ReadOnlySpan<byte>((byte*)data, (int)size));
            if (processed != IntPtr.Zero) *(uint*)processed = size;
            return NativeHresult.S_OK;
        }

        private static int OutStreamFlush(nint self)
        {
            Get(self)._stream.Flush();
            return NativeHresult.S_OK;
        }

        protected override void DisposeManaged() => _stream.Dispose();
    }

    private unsafe sealed class ExtractCallbackProxy : NativeComObject
    {
        private static readonly IntPtr Vtbl;
        private readonly ExtractContext _ctx;
        private readonly IProgress<ExtractProgress>? _progress;
        private ManagedOutStream? _active;
        private uint _currentIndex;

        static ExtractCallbackProxy()
        {
            var methods = new Delegate[]
            {
                new NativeDelegates.NativeQueryInterfaceFn(CallbackQueryInterface),
                new NativeDelegates.NativeAddRefFn(CallbackAddRef),
                new NativeDelegates.NativeReleaseFn(CallbackRelease),
                new NativeDelegates.NativeSetTotalFn(CallbackSetTotal),
                new NativeDelegates.NativeSetCompletedFn(CallbackSetCompleted),
                new NativeDelegates.NativeGetStreamFn(CallbackGetStream),
                new NativeDelegates.NativePrepareOperationFn(CallbackPrepareOperation),
                new NativeDelegates.NativeSetOperationResultFn(CallbackSetOperationResult),
            };
            KeepAlive.Add(methods);
            Vtbl = BuildVtbl(8, methods);
        }

        public ExtractCallbackProxy(ExtractContext ctx, IProgress<ExtractProgress>? progress) : base(Vtbl, null)
        {
            _ctx = ctx;
            _progress = progress;
            SetInstance(this);
        }

        private static int CallbackQueryInterface(nint self, ref Guid iid, out nint ppv)
        {
            if (iid == IidIUnknown || iid == IidArchiveExtractCallback)
            {
                ppv = self;
                return NativeHresult.S_OK;
            }
            ppv = IntPtr.Zero;
            return NativeHresult.E_NOINTERFACE;
        }

        private static ExtractCallbackProxy Get(nint self) => (ExtractCallbackProxy)Instance(self);

        private static int CallbackAddRef(nint self) => Get(self).AddRef();

        private static int CallbackRelease(nint self) => Get(self).Release();

        private static int CallbackSetTotal(nint self, ulong total) => NativeHresult.S_OK;

        private static int CallbackSetCompleted(nint self, nint completeValue) => NativeHresult.S_OK;

        private static int CallbackGetStream(nint self, uint index, nint outStreamPtr, int askExtractMode)
        {
            var cb = Get(self);
            if (cb._ctx.IsCancellationRequested) return NativeHresult.E_ABORT;

            cb._currentIndex = index;
            var fullPath = cb._ctx.TryGetFullPath(index);
            if (fullPath is null)
            {
                *(nint*)outStreamPtr = IntPtr.Zero;
                return NativeHresult.S_OK;
            }

            try
            {
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
                cb._active = new ManagedOutStream(fs, cb._ctx);
                *(nint*)outStreamPtr = cb._active.Self;
                return NativeHresult.S_OK;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                *(nint*)outStreamPtr = IntPtr.Zero;
                return NativeHresult.E_FAIL;
            }
        }

        private static int CallbackPrepareOperation(nint self, int askExtractMode) => NativeHresult.S_OK;

        private static int CallbackSetOperationResult(nint self, int operationResult)
        {
            var cb = Get(self);
            cb._active?.Dispose();
            cb._active = null;
            if (operationResult == NativeHresult.S_OK)
            {
                cb._ctx.OnFileExtracted(cb._currentIndex);
                var (files, bytes) = cb._ctx.Counters;
                cb._progress?.Report(new ExtractProgress { FilesDone = files, BytesDone = bytes });
            }
            return NativeHresult.S_OK;
        }

        protected override void DisposeManaged()
        {
            _active?.Dispose();
            _active = null;
        }
    }
}