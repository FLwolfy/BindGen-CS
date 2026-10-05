using System;

namespace BGCS.CppAst.Parsing;

using System.Runtime.InteropServices;
using ClangSharp.Interop;

/// <summary>
/// Defines the public struct <c>DGCHandle</c>.
/// </summary>
internal unsafe struct DGCHandle<T> : IDisposable where T : class
{
    GCHandle m_handle;
    /// <summary>
    /// Initializes a new instance of <see cref="DGCHandle{T}"/>.
    /// </summary>
    public DGCHandle(T obj)
    {
        this.m_handle = GCHandle.Alloc(obj, GCHandleType.Normal);
    }

    /// <summary>
    /// Executes public operation <c>DGCHandle</c>.
    /// </summary>
    public DGCHandle(void* ptr)
    {
        this.m_handle = GCHandle.FromIntPtr((nint)ptr);
    }

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public T value => (T)this.m_handle.Target!;

    /// <summary>
    /// Executes public operation <c>Dispose</c>.
    /// </summary>
    public void Dispose()
    {
        this.m_handle.Free();
    }

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public static implicit operator void*(in DGCHandle<T> h) => (void*)(nint)h.m_handle;
    /// <summary>
    /// Executes public operation <c>DGCHandle</c>.
    /// </summary>
    public static implicit operator DGCHandle<T>(void* ptr) => new(ptr);
    /// <summary>
    /// Executes public operation <c>T</c>.
    /// </summary>
    public static implicit operator T(in DGCHandle<T> h) => h.value;
    /// <summary>
    /// Executes public operation <c>CXClientData</c>.
    /// </summary>
    public static implicit operator CXClientData(in DGCHandle<T> h) => new((nint)h.m_handle);
    /// <summary>
    /// Executes public operation <c>ObjFrom</c>.
    /// </summary>
    public static T ObjFrom(void* ptr)
    {
        GCHandle handle = GCHandle.FromIntPtr((nint)ptr);
        return (T)handle.Target!;
    }
}
