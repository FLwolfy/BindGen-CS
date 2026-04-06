using System;
using System.Collections.Generic;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public unsafe class FunctionTableAndGLExtensionFlowTests
{
    [Fact]
    public void Load_WhenExportExists_ShouldStoreAddressInVTable()
    {
        FakeGLContext context = new();
        context.ProcMap["A"] = (nint)0x1111;
        using FunctionTable table = new(context, 1);

        table.Load(0, "A");

        Assert.Equal((nint)0x1111, (nint)table[0]);
    }

    [Fact]
    public void Load_WhenExportMissing_ShouldStoreNullPointer()
    {
        FakeGLContext context = new();
        using FunctionTable table = new(context, 1);
        table[0] = (void*)0x1234;

        table.Load(0, "Missing");

        Assert.Equal((nint)0, (nint)table[0]);
    }

    [Fact]
    public void Indexer_SetAndGet_ShouldRoundTripAddress()
    {
        FakeGLContext context = new();
        using FunctionTable table = new(context, 1);

        table[0] = (void*)0x4321;

        Assert.Equal((nint)0x4321, (nint)table[0]);
    }

    [Fact]
    public void GLExtension_InitAndDispose_ShouldLoadTableAndDisposeContext()
    {
        FakeGLContext context = new();
        context.ProcMap["ExtFnA"] = (nint)0x88;
        context.ProcMap["ExtFnB"] = (nint)0x99;
        using (ProbeExtension extension = new())
        {
            extension.Init(context);

            Assert.Equal((nint)0x88, extension.LoadedA);
            Assert.Equal((nint)0x99, extension.LoadedB);
        }

        Assert.True(context.DisposeCount >= 1);
    }

    private sealed class ProbeExtension : GLExtension
    {
        public ProbeExtension() : base(2)
        {
        }

        public nint LoadedA { get; private set; }

        public nint LoadedB { get; private set; }

        public override bool IsSupported(IGLContext context)
        {
            return context.IsExtensionSupported("Probe");
        }

        protected override void InitTable(FunctionTable funcTable)
        {
            funcTable.Load(0, "ExtFnA");
            funcTable.Load(1, "ExtFnB");
            LoadedA = (nint)funcTable[0];
            LoadedB = (nint)funcTable[1];
        }
    }

    private sealed class FakeGLContext : IGLContext
    {
        public Dictionary<string, nint> ProcMap { get; } = new(StringComparer.Ordinal);

        public int DisposeCount { get; private set; }

        public nint Handle => (nint)0x1;

        public bool IsCurrent => true;

        public void Dispose()
        {
            DisposeCount++;
        }

        public nint GetProcAddress(string procName)
        {
            return ProcMap.TryGetValue(procName, out nint value) ? value : 0;
        }

        public bool TryGetProcAddress(string procName, out nint procAddress)
        {
            return ProcMap.TryGetValue(procName, out procAddress);
        }

        public bool IsExtensionSupported(string extensionName)
        {
            return extensionName == "Probe";
        }

        public void MakeCurrent()
        {
        }

        public void SwapBuffers()
        {
        }

        public void SwapInterval(int interval)
        {
        }
    }
}
