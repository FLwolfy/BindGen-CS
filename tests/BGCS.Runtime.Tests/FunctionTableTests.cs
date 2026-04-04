using System;
using System.Collections.Generic;
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
