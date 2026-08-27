using System.Runtime.InteropServices;

namespace UltimateWardrobe.Archives.Native;

internal static class NativeHresult
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_ABORT = unchecked((int)0x80004004);
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int E_FAIL = unchecked((int)0x80004005);
}

internal static unsafe class NativeHelper
{
    public static T GetExport<T>(IntPtr lib, string name) where T : Delegate
    {
        IntPtr p = NativeLibrary.GetExport(lib, name);
        if (p == IntPtr.Zero) throw new InvalidOperationException($"Export '{name}' not found in native library.");
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    public static T VtblFn<T>(IntPtr obj, int slot) where T : Delegate
    {
        IntPtr vtbl = *(IntPtr*)obj;
        IntPtr fn = *(IntPtr*)(vtbl + slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    public static string? ReadPropVariantString(nint pv)
    {
        ushort vt = *(ushort*)pv;
        if (vt == 0x0008)
        {
            nint bstr = *(nint*)(pv + 8);
            if (bstr == 0) return null;
            string s = Marshal.PtrToStringUni(bstr) ?? string.Empty;
            Marshal.FreeBSTR(bstr);
            return s;
        }

        if (vt == 0x001F)
        {
            nint p = *(nint*)(pv + 8);
            if (p == 0) return null;
            string s = Marshal.PtrToStringUni(p) ?? string.Empty;
            Marshal.FreeCoTaskMem(p);
            return s;
        }

        return null;
    }
}

/// <summary>
/// Base for managed objects exposed to native code through a hand-built unmanaged vtable.
/// Native layout of _self: [ vtable ptr (IntPtr) | GCHandle (IntPtr) ].
/// </summary>
internal abstract unsafe class NativeComObject : IDisposable
{
    public static readonly List<object> KeepAlive = new();

    private IntPtr _self;
    private GCHandle _handle;
    private int _refCount = 1;

    protected NativeComObject(IntPtr vtbl, object? instance)
    {
        _self = Marshal.AllocHGlobal(2 * IntPtr.Size);
        Marshal.WriteIntPtr(_self, 0, vtbl);
        if (instance is not null) SetInstance(instance);
    }

    public IntPtr Self => _self;

    protected void SetInstance(object instance)
    {
        if (_handle.IsAllocated) _handle.Free();
        _handle = GCHandle.Alloc(instance);
        Marshal.WriteIntPtr(_self, IntPtr.Size, GCHandle.ToIntPtr(_handle));
    }

    public static IntPtr BuildVtbl(int count, Delegate[] methods)
    {
        var v = Marshal.AllocHGlobal(IntPtr.Size * count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                Marshal.WriteIntPtr(v, i * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(methods[i]));
            }
            return v;
        }
        catch
        {
            Marshal.FreeHGlobal(v);
            throw;
        }
    }

    public static object Instance(IntPtr self)
    {
        var h = GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size));
        return h.Target ?? throw new InvalidOperationException("Native COM object handle has no target.");
    }

    public static int QueryInterfaceImpl(nint self, ref Guid iid, out nint ppv)
    {
        ppv = IntPtr.Zero;
        return NativeHresult.E_NOINTERFACE;
    }

    public int AddRef() => Interlocked.Increment(ref _refCount);

    public int Release() => Interlocked.Decrement(ref _refCount);

    protected virtual void DisposeManaged()
    {
    }

    public virtual void Dispose()
    {
        DisposeManaged();
        if (_handle.IsAllocated) _handle.Free();
        if (_self != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_self);
            _self = IntPtr.Zero;
        }
    }
}