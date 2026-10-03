using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public class FunctionTableTests
{
    [Fact]
    public void Constructor_WithNativeContext_ShouldSetLength()
    {
        FakeContext context = new();
        using FunctionTable table = new(context, 4);

        Assert.Equal(4, table.Length);
    }

    [Fact]
    public void Load_ShouldQueryContextForEachExport()
    {
        FakeContext context = new();
        context.ProcMap["A"] = (nint)0x1;

        using FunctionTable table = new(context, 2);
        table.Load(0, "A");
        table.Load(1, "B");

        Assert.Equal(["A", "B"], context.RequestedNames);
    }

    [Fact]
    public void Resize_ShouldUpdateLength()
    {
        FakeContext context = new();
        using FunctionTable table = new(context, 2);

        table.Resize(5);

        Assert.Equal(5, table.Length);
    }

    [Fact]
    public void Free_ShouldDisposeNativeContext()
    {
        FakeContext context = new();
        FunctionTable table = new(context, 1);

        table.Free();

        Assert.True(context.DisposeCallCount >= 1);
    }

    [Fact]
    public unsafe void BorrowedTable_DisposePreservesCallerStorageAndRejectsResize()
    {
        void** storage = (void**)Marshal.AllocHGlobal(sizeof(void*));
        try
        {
            storage[0] = (void*)123;
            FunctionTable table = new(storage, 1);
            Assert.Equal((nint)123, (nint)table[0]);
            Assert.Throws<InvalidOperationException>(() => table.Resize(2));
            table.Dispose();
            table.Dispose();
            Assert.Equal((nint)123, (nint)storage[0]);
            storage[0] = (void*)456;
            Assert.Equal((nint)456, (nint)storage[0]);
            Assert.Throws<ObjectDisposedException>(() => table.Load(0, "A"));
        }
        finally
        {
            Marshal.FreeHGlobal((nint)storage);
        }
    }

    [Fact]
    public unsafe void OwnedTable_ResizeRetainsEntriesClearsNewSlotsAndDisposesOnce()
    {
        FakeContext context = new();
        FunctionTable table = new(context, 1);
        table[0] = (void*)123;
        table.Resize(3);
        Assert.Equal((nint)123, (nint)table[0]);
        Assert.Equal(0, (nint)table[1]);
        Assert.Equal(0, (nint)table[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Load(3, "A"));
        table.Free();
        table.Dispose();
        Assert.Equal(1, context.DisposeCallCount);
        Assert.Equal(0, table.Length);
        Assert.Throws<ObjectDisposedException>(() => table.Resize(1));
    }

    private sealed class FakeContext : INativeContext
    {
        public Dictionary<string, nint> ProcMap { get; } = new(StringComparer.Ordinal);

        public List<string> RequestedNames { get; } = [];

        public int DisposeCallCount { get; private set; }

        public nint GetProcAddress(string procName)
        {
            RequestedNames.Add(procName);
            return ProcMap.TryGetValue(procName, out nint value) ? value : 0;
        }

        public bool TryGetProcAddress(string procName, out nint procAddress)
        {
            RequestedNames.Add(procName);
            return ProcMap.TryGetValue(procName, out procAddress);
        }

        public bool IsExtensionSupported(string extensionName)
        {
            return false;
        }

        public void Dispose()
        {
            DisposeCallCount++;
        }
    }
}
