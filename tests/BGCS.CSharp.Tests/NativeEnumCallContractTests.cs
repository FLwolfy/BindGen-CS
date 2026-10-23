using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class NativeEnumCallContractTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task WideNativeEnumsKeepTheirUnderlyingWidthAndExactPointerChannels(
        BindingImportMode mode,
        bool unsigned
    ) {
        InteropType integral = new(new("integral"), unsigned ? "unsigned long long" : "long long", NativeTypeCategory.Primitive, 8, 8)
        { primitiveKind = unsigned ? "UnsignedLongLong" : "LongLong", isSigned = !unsigned };
        string value = unsigned ? "18446744073709551615" : "-9223372036854775808";
        InteropType flags = new(new("flags"), "Flags", NativeTypeCategory.Enumeration, 8, 8)
        { declarationName = "Flags", elementType = integral.id, enumValues = [new(new("limit"), "Limit", value, value)] };
        InteropType pointer = new(new("pointer"), "Flags*", NativeTypeCategory.Pointer, 4, 4) { elementType = flags.id };
        InteropFunction function = new(new("transform"), "transform", flags.id,
            [new(new("value"), "value", flags.id), new(new("output"), "output", pointer.id)], "C");
        InteropModule module = new("enum-call", "emscripten-wasm32-emscripten", 4, [integral, flags, pointer], [function], [])
        { byteOrder = NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.NativeEnumCalls", "Api"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native-enum", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpStoragePlan storage = Assert.Single(plan.storage);
        Assert.Equal(unsigned ? "ulong" : "long", storage.enumUnderlyingType);
        Assert.Equal(8, storage.size);
        Assert.Equal("Flags", Assert.Single(plan.methods).returnType);
        Assert.Equal(new[] { "Flags", "Flags*" }, plan.methods[0].parameterTypes);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        string enumSource = sink.sources["Types/Flags.g.cs"];
        string changed = enumSource.Replace(unsigned ? "Flags : ulong" : "Flags : long", "Flags : int", StringComparison.Ordinal);
        Assert.NotEqual(enumSource, changed);
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(source => (source.Key, source.Key == "Types/Flags.g.cs" ? changed : source.Value)), request.libraryName));
        // This is a synthetic wasm32 model/SDK compilation regression. It does not
        // certify invocation in a browser or on a 32-bit native runtime.
        await ValueReceiverTests.CompileWithSdkAsync(sink, """
            namespace Generated.NativeEnumCalls;
            public static unsafe class Consumer {
                public static Flags Call(Api api,Flags value,Flags* output) => api.Raw.Transform(value,output);
            }
            """, sourceRuntime: false);
    }
}
