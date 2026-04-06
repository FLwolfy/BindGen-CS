using System;
namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using System.Runtime.InteropServices;

/// <summary>
/// Defines the public struct <c>DGCHandle</c>.
/// </summary>
public unsafe struct DGCHandle<T> : IDisposable where T : class
{
    GCHandle handle;

    /// <summary>
    /// Initializes a new instance of <see cref="DGCHandle"/>.
    /// </summary>
    public DGCHandle(T obj)
    {
        handle = GCHandle.Alloc(obj, GCHandleType.Normal);
    }

    /// <summary>
    /// Executes public operation <c>DGCHandle</c>.
    /// </summary>
    public DGCHandle(void* ptr)
    {
        handle = GCHandle.FromIntPtr((nint)ptr);
    }

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public T Value => (T)handle.Target!;

    /// <summary>
    /// Executes public operation <c>Dispose</c>.
    /// </summary>
    public void Dispose()
    {
        handle.Free();
    }

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public static implicit operator void*(in DGCHandle<T> h) => (void*)(nint)h.handle;

    /// <summary>
    /// Executes public operation <c>DGCHandle</c>.
    /// </summary>
    public static implicit operator DGCHandle<T>(void* ptr) => new(ptr);

    /// <summary>
    /// Executes public operation <c>T</c>.
    /// </summary>
    public static implicit operator T(in DGCHandle<T> h) => h.Value;

    /// <summary>
    /// Executes public operation <c>CXClientData</c>.
    /// </summary>
    public static implicit operator CXClientData(in DGCHandle<T> h) => new((nint)h.handle);

    /// <summary>
    /// Executes public operation <c>ObjFrom</c>.
    /// </summary>
    public static T ObjFrom(void* ptr)
    {
        GCHandle handle = GCHandle.FromIntPtr((nint)ptr);
        return (T)handle.Target!;
    }
}
