using System.Runtime.InteropServices;

namespace UltimateWardrobe.Archives.Native;

internal static unsafe class NativeDelegates
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int CreateObjectFn(ref Guid clsid, ref Guid iid, out IntPtr obj);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int IUnknownReleaseFn(nint self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int IInArchiveOpenFn(nint self, nint stream, nint maxCheckStartPosition, nint openCallback);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int IInArchiveCloseFn(nint self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int IInArchiveGetCountFn(nint self, uint* count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int IInArchiveGetPropertyFn(nint self, uint index, uint propId, nint value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int IInArchiveExtractFn(nint self, nint indices, uint numItems, int testMode, nint callback);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeQueryInterfaceFn(nint self, ref Guid iid, out nint ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeAddRefFn(nint self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeReleaseFn(nint self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeReadFn(nint self, nint data, uint size, nint processed);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeSeekFn(nint self, long offset, uint origin, nint newPos);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeWriteFn(nint self, nint data, uint size, nint processed);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeFlushFn(nint self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeSetTotalFn(nint self, ulong total);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeSetCompletedFn(nint self, nint completeValue);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeGetStreamFn(nint self, uint index, nint outStreamPtr, int askExtractMode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativePrepareOperationFn(nint self, int askExtractMode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeSetOperationResultFn(nint self, int operationResult);
}