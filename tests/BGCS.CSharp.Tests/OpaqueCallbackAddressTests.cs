using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class OpaqueCallbackAddressTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task AnUnsupportedCallbackInvocationRetainsOnlyAnOpaqueAddress(bool variadic, bool prototype)
    {
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType signature = new(new("callback-signature"), "int(int, ...)", NativeTypeCategory.Function, null, null)
        { returnType = integer.id, parameterTypes = [integer.id], isVariadic = variadic, hasPrototype = prototype, callingConvention = "C" };
        InteropType pointer = new(new("callback-address"), "native_callback", NativeTypeCategory.Pointer, 8, 8) { elementType = signature.id };
        InteropModule module = new("fixture", "windows-x64-msvc", 8, [integer, signature, pointer],
            [new(new("get"), "get_callback", pointer.id, [], "C"), new(new("use"), "use_callback", integer.id, [new(new("callback"), "callback", pointer.id)], "C")], []);
        var model = new DefaultManagedProjection().Project(module, new("Generated.Callbacks", "CallbackApi"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("fixture", sink) { importMode = BindingImportMode.LibraryImport };
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        Assert.Equal(signature.id, Assert.Single(plan.opaqueCallables).nativeType);
        Assert.Equal("nint", plan.types.Single(type => type.nativeType == pointer.id).syntax);
        Assert.Equal("nint", plan.methods.Single(method => method.exportName == "get_callback").returnType);
        Assert.Equal("nint", Assert.Single(plan.methods.Single(method => method.exportName == "use_callback").parameterTypes));
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        Assert.DoesNotContain(sink.sources.Values, source => source.Contains("delegate* unmanaged[Cdecl]<int, int>", StringComparison.Ordinal));
        Assert.Contains(sink.sources.Values, source => source.Contains("nint Function", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContradictoryPointerWidthCannotBecomeAnOpaqueCallbackAddress()
    {
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType signature = new(new("callback-signature"), "int(...)", NativeTypeCategory.Function, null, null)
        { returnType = integer.id, isVariadic = true, callingConvention = "C" };
        InteropType pointer = new(new("callback-address"), "native_callback", NativeTypeCategory.Pointer, 4, 4)
        { elementType = signature.id, source = new("include/callbacks.h", 17, 5) };
        InteropModule module = new("fixture", "windows-x64-msvc", 8, [integer, signature, pointer], [], []);
        var model = new DefaultManagedProjection().Project(module, new("Generated.Callbacks", "CallbackApi"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        CSharpPlanningException failure = await Assert.ThrowsAsync<CSharpPlanningException>(async () => await new CSharpBackend().EmitAsync(model, new("fixture", sink), CancellationToken.None));
        Assert.Equal(pointer.id, failure.nativeIdentity);
        Assert.Equal(pointer.source, failure.diagnostic.location);
        Assert.Equal("BGCS-CSHARP-ABI-PLAN", failure.diagnostic.code);
        Assert.Contains("native size=4", failure.Message);
        Assert.Contains("required pointer size/alignment=8", failure.Message);
        Assert.Empty(sink.sources);
    }
}
