using System;
namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using System.Runtime.InteropServices;

public unsafe struct DGCHandle<T> : IDisposable where T : class
{
    GCHandle handle;

    public DGCHandle(T obj)
    {
        handle = GCHandle.Alloc(obj, GCHandleType.Normal);
    }

    public DGCHandle(void* ptr)
    {
        handle = GCHandle.FromIntPtr((nint)ptr);
    }

    public T Value => (T)handle.Target!;

    public void Dispose()
    {
        handle.Free();
    }

    public static implicit operator void*(in DGCHandle<T> h) => (void*)(nint)h.handle;

    public static implicit operator DGCHandle<T>(void* ptr) => new(ptr);

    public static implicit operator T(in DGCHandle<T> h) => h.Value;

    public static implicit operator CXClientData(in DGCHandle<T> h) => new((nint)h.handle);

    public static T ObjFrom(void* ptr)
    {
        GCHandle handle = GCHandle.FromIntPtr((nint)ptr);
        return (T)handle.Target!;
    }
}
