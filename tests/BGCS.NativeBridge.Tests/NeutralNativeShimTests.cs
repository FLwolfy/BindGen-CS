using System;
using System.IO;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.NativeBridge.Shims;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralNativeShimTests
{
    [Fact]
    public void ExplicitShimInputs_NormalizeAndRequireActualFixedCLinkageExports()
    {
        string pathBase = Path.GetFullPath(Path.GetTempPath());
        NativeShimPlan plan = new NativeShimPlanner().Plan(new() { id = "shim", publicHeaders = ["shim.h"], sourceFiles = ["shim.cpp"], exports = ["shim_value"] }, pathBase);
        Assert.Equal(Path.GetFullPath("shim.h", pathBase), plan.publicHeaders[0]);
        Assert.Equal(EvidenceLevel.UserAsserted, plan.evidence.level);
        NativeDeclaration callable = new(new("callable"), "Function", "shim_value", "shim_value", null, new("shim.h", 1, 1)) { linkage = "C" };
        NativeModule source = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [], [callable], []);
        Assert.Equal(new[] { callable.id }, new NativeShimPlanner().SelectCallables(source, plan));
        NativeModule cppOnly = new(source.targetId, source.triple, source.pointerSize, [], [callable with { linkage = "Cpp" }], []);
        Assert.Throws<InvalidOperationException>(() => new NativeShimPlanner().SelectCallables(cppOnly, plan));
    }

    [Fact]
    public void DuplicatePathsAndExports_AreRejected()
    {
        string pathBase = Path.GetFullPath(Path.GetTempPath());
        Assert.Throws<ArgumentException>(() => new NativeShimPlanner().Plan(new() { id = "shim", publicHeaders = ["shim.h", "./shim.h"], exports = ["shim_value"] }, pathBase));
        Assert.Throws<ArgumentException>(() => new NativeShimPlanner().Plan(new() { id = "shim", publicHeaders = ["shim.h"], exports = ["shim_value", "shim_value"] }, pathBase));
    }
}
