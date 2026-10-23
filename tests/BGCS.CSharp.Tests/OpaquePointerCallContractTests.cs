using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class OpaquePointerCallContractTests
{
    [Theory]
    [InlineData("emscripten-wasm32-emscripten", 4, BindingImportMode.FunctionTable)]
    [InlineData("emscripten-wasm32-emscripten", 4, BindingImportMode.DllImport)]
    [InlineData("emscripten-wasm32-emscripten", 4, BindingImportMode.LibraryImport)]
    [InlineData("windows-x64-msvc", 8, BindingImportMode.FunctionTable)]
    [InlineData("windows-x64-msvc", 8, BindingImportMode.DllImport)]
    [InlineData("windows-x64-msvc", 8, BindingImportMode.LibraryImport)]
    [InlineData("macos-arm64-darwin", 8, BindingImportMode.FunctionTable)]
    [InlineData("macos-arm64-darwin", 8, BindingImportMode.DllImport)]
    [InlineData("macos-arm64-darwin", 8, BindingImportMode.LibraryImport)]
    public async Task OpaqueAliasesKeepNativePointerChannelsOutsideTheirAddressWrapper(string target, int pointerSize, BindingImportMode mode)
    {
        InteropType record = new(new("opaque"), "Window", NativeTypeCategory.Record, null, null) { recordKind = "Struct" };
        InteropType pointer = new(new("pointer"), "Window*", NativeTypeCategory.Pointer, pointerSize, pointerSize) { elementType = record.id };
        InteropType alias = new(new("alias"), "NativeWindow", NativeTypeCategory.Alias, pointerSize, pointerSize) { elementType = pointer.id };
        InteropFunction next = new(new("next"), "window_next", alias.id, [new(new("window"), "window", alias.id)], "C");
        InteropModule interop = new("opaque-address", target, pointerSize, [record, pointer, alias], [next], []) { byteOrder = NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(interop, new("Generated.OpaqueCalls", "Api")
        {
            pointerCarriers = [new("NativeWindow", "WindowAddress")],
            pointerFunctions = [new("window_next") { name = "NextAddress", parameters = [new("window", "WindowAddress")], returns = "WindowAddress" }]
        }, CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpMethodPlan native = Assert.Single(plan.methods);
        Assert.EndsWith("*", native.returnType, StringComparison.Ordinal);
        Assert.Equal(native.returnType, Assert.Single(native.parameterTypes));
        Assert.DoesNotContain("WindowAddress", native.returnType, StringComparison.Ordinal);
        Assert.Equal(pointerSize, interop.GetType(alias.id).size);
        Assert.Single(plan.pointerFunctions);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string all = string.Join("\n", sink.sources.Values);
        Assert.DoesNotContain("Unsafe.As<", all, StringComparison.Ordinal);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        // Synthetic target facts and an SDK compile prove representation and
        // source correctness; real ARM64/wasm invocation is a separate gate.
        await ValueReceiverTests.CompileWithSdkAsync(sink, """
            namespace Generated.OpaqueCalls;
            public static class Consumer {
                public static WindowAddress Forward(Api api,WindowAddress value)=>api.NextAddress(value);
            }
            """, sourceRuntime: false);
    }
}
