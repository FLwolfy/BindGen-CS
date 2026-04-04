using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BGCS.Runtime.COM;
using Xunit;

namespace BGCS.Runtime.COM.Tests;

public unsafe class ComLifecycleTests
{
    [Fact]
    public void ComObject_Dispose_ShouldReleaseExactlyOnceAndBeIdempotent()
    {
        using FakeComServer fake = new(initialRefCount: 1);

        ComObject obj = ComObject.FromPtr(fake.Pointer)!;
        obj.Dispose();
        obj.Dispose();

        Assert.True(obj.Handle == null);
        Assert.Equal(1, fake.State.ReleaseCalls);
    }

    [Fact]
    public void ComObject_QueryInterface_ShouldIncrementRefAndReturnObject()
    {
        using FakeComServer fake = new(initialRefCount: 1);

        ComObject obj = ComObject.FromPtr(fake.Pointer)!;
        Guid iid = IUnknown.Guid;
        int hr = obj.QueryInterface(ref iid, out ComObject? queried);

        Assert.Equal(0, hr);
        Assert.NotNull(queried);
        Assert.Equal(1, fake.State.QueryCalls);
        Assert.Equal(1, fake.State.AddRefCalls);

        queried!.Dispose();
        obj.Dispose();

        Assert.Equal(2, fake.State.ReleaseCalls);
    }

    [Fact]
    public void ComObject_QueryInterface_WhenUnsupported_ShouldReturnFailureAndNullObject()
    {
        using FakeComServer fake = new(initialRefCount: 1);

        ComObject obj = ComObject.FromPtr(fake.Pointer)!;
        Guid iid = UnsupportedCom.Guid;
        int hr = obj.QueryInterface(ref iid, out ComObject? queried);

        Assert.Equal(unchecked((int)0x80004002), hr);
        Assert.Null(queried);
        Assert.Equal(1, fake.State.QueryCalls);
        Assert.Equal(0, fake.State.AddRefCalls);

        obj.Dispose();
        Assert.Equal(1, fake.State.ReleaseCalls);
    }

    [Fact]
    public void ComPtr_ReleaseDisposeDetach_ShouldUseIUnknownVtable()
    {
        using FakeComServer fake = new(initialRefCount: 3);

        ComPtr<IUnknown> ptr = new(fake.Pointer);

        uint afterRelease = ptr.Release();
        Assert.Equal((uint)2, afterRelease);

        ptr.Dispose();
        Assert.Equal(2, fake.State.ReleaseCalls);

        IUnknown* detached = ptr.Detach();
        Assert.True(detached == null);
        Assert.True(ptr.Handle == null);

        ptr.Dispose();
        Assert.Equal(2, fake.State.ReleaseCalls);
    }

    [Fact]
    public void ComPtr_Dispose_ShouldBeIdempotentAndClearHandle()
    {
        using FakeComServer fake = new(initialRefCount: 2);

        ComPtr<IUnknown> ptr = new(fake.Pointer);
        ptr.Dispose();
        ptr.Dispose();

        Assert.True(ptr.Handle == null);
        Assert.Equal(1, fake.State.ReleaseCalls);
    }

    [Fact]
    public void ComPtr_QueryInterface_Generic_ShouldRoundTripThroughVtable()
    {
        using FakeComServer fake = new(initialRefCount: 1);

        ComPtr<IUnknown> ptr = new(fake.Pointer);
        HResult hr = ptr.QueryInterface<IUnknown>(out ComPtr<IUnknown> queried);

        Assert.True(hr.IsSuccess);
        Assert.True(queried.Handle == fake.Pointer);
        Assert.Equal(1, fake.State.QueryCalls);
        Assert.Equal(1, fake.State.AddRefCalls);

        queried.Dispose();
        ptr.Dispose();
        Assert.Equal(2, fake.State.ReleaseCalls);
    }

    [Fact]
    public void ComPtr_QueryInterface_GenericUnsupported_ShouldReturnFailureAndNullHandle()
    {
        using FakeComServer fake = new(initialRefCount: 1);

        ComPtr<IUnknown> ptr = new(fake.Pointer);
        HResult hr = ptr.QueryInterface<UnsupportedCom>(out ComPtr<UnsupportedCom> queried);

        Assert.True(hr.IsFailure);
        Assert.True(queried.Handle == null);
        Assert.Equal(1, fake.State.QueryCalls);
        Assert.Equal(0, fake.State.AddRefCalls);

        ptr.Dispose();
        Assert.Equal(1, fake.State.ReleaseCalls);
    }

    private sealed class FakeComServer : IDisposable
    {
        private static readonly Dictionary<nint, ComState> States = new();
        private static readonly object Gate = new();

        private readonly void** vtable;
        private readonly IUnknown* instance;

        public FakeComServer(uint initialRefCount)
        {
            vtable = (void**)NativeMemory.Alloc((nuint)3, (nuint)sizeof(void*));
            vtable[0] = (void*)(delegate* unmanaged[Stdcall]<IUnknown*, Guid*, void**, int>)&QueryInterfaceImpl;
            vtable[1] = (void*)(delegate* unmanaged[Stdcall]<IUnknown*, uint>)&AddRefImpl;
            vtable[2] = (void*)(delegate* unmanaged[Stdcall]<IUnknown*, uint>)&ReleaseImpl;

            instance = (IUnknown*)NativeMemory.Alloc(1, (nuint)sizeof(IUnknown));
            instance->LpVtbl = vtable;

            lock (Gate)
            {
                States[(nint)instance] = new ComState(initialRefCount, [IUnknown.Guid]);
            }
        }

        public IUnknown* Pointer => instance;

        public ComState State
        {
            get
            {
                lock (Gate)
                {
                    return States[(nint)instance];
                }
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int QueryInterfaceImpl(IUnknown* self, Guid* riid, void** ppvObject)
        {
            lock (Gate)
            {
                ComState state = States[(nint)self];
                state.QueryCalls++;

                Guid iid = *riid;
                if (!state.SupportedInterfaces.Contains(iid))
                {
                    *ppvObject = null;
                    return unchecked((int)0x80004002); // E_NOINTERFACE
                }

                state.AddRefCalls++;
                state.RefCount++;
            }

            *ppvObject = self;
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint AddRefImpl(IUnknown* self)
        {
            lock (Gate)
            {
                ComState state = States[(nint)self];
                state.AddRefCalls++;
                state.RefCount++;
                return state.RefCount;
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint ReleaseImpl(IUnknown* self)
        {
            lock (Gate)
            {
                ComState state = States[(nint)self];
                state.ReleaseCalls++;
                if (state.RefCount > 0)
                {
                    state.RefCount--;
                }
                return state.RefCount;
            }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                States.Remove((nint)instance);
            }

            NativeMemory.Free(instance);
            NativeMemory.Free(vtable);
        }
    }

    private sealed class ComState
    {
        public ComState(uint initialRefCount, HashSet<Guid> supportedInterfaces)
        {
            RefCount = initialRefCount;
            SupportedInterfaces = supportedInterfaces;
        }

        public uint RefCount { get; set; }

        public HashSet<Guid> SupportedInterfaces { get; }

        public int QueryCalls { get; set; }

        public int AddRefCalls { get; set; }

        public int ReleaseCalls { get; set; }
    }

    [Guid("11111111-2222-3333-4444-555555555555")]
    private struct UnsupportedCom : IComObject, IComObject<UnsupportedCom>
    {
        public static readonly Guid Guid = new("11111111-2222-3333-4444-555555555555");

        public unsafe void** LpVtbl;

        public unsafe void*** AsVtblPtr()
        {
            return (void***)Unsafe.AsPointer(ref Unsafe.AsRef(in this));
        }
    }
}
