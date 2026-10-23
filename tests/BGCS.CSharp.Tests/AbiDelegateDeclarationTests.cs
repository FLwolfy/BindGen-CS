using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class AbiDelegateDeclarationTests
{
    [Fact]
    public async Task StandaloneNamedTypedefHonorsCallbackNamingWithoutInventingNativeRetention()
    {
        ManagedApiModel model = Model(withMethod: false);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpAbiDelegatePlan declaration = Assert.Single(plan.abiDelegates);
        Assert.Equal("ManagedNativeCallbackHandler", declaration.name);
        Assert.Equal("int", declaration.returnType);
        Assert.Equal("int", Assert.Single(declaration.parameterTypes));
        Assert.Empty(plan.methods);
        Assert.Empty(plan.callScopedCallbacks);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string text = sink.sources["Types/ManagedNativeCallbackHandler.g.cs"];
        Assert.Contains("public unsafe delegate int ManagedNativeCallbackHandler(int argument0)", text);
        Assert.Contains("UnmanagedFunctionPointer", text);
    }

    [Fact]
    public void CoreClrCallScopeReusesTheOriginalExactNamedAbiDelegate()
    {
        ManagedApiModel model = Model(withMethod: true);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()));
        CSharpCallbackInputPlan callback = Assert.Single(Assert.Single(plan.callScopedCallbacks).callbacks);
        Assert.Equal("global::Generated.Delegates.ManagedNativeCallbackHandler", callback.abiDelegateType);
        Assert.Equal(callback.abiDelegateType, plan.callScopedCallbacks[0].signature.parameterTypes[0]);
    }

    [Fact]
    public async Task StandaloneDeclarationsCanBeOmittedWithoutChangingRawPointersOrTheCheckedCallScope()
    {
        ManagedApiModel original = Model(withMethod: true);
        ManagedApiModel model = new DefaultManagedProjection().Project(original.interop, new("Generated.Delegates", "NativeApi")
            { emitCallbackDelegates = false }, CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        Assert.Empty(plan.abiDelegates);
        Assert.Contains("delegate* unmanaged[Cdecl]<int, int>", plan.methods[0].parameterTypes[0]);
        Assert.Null(Assert.Single(Assert.Single(plan.callScopedCallbacks).callbacks).abiDelegateType);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        Assert.DoesNotContain(sink.sources.Keys, path => path.StartsWith("Types/", StringComparison.Ordinal));
        Assert.Contains(sink.sources.Keys, path => path.StartsWith("Callbacks/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("result")]
    [InlineData("argument")]
    [InlineData("convention")]
    [InlineData("visibility")]
    public async Task ArtifactProcessorsCannotReplaceAnExactNamedDelegatePrototype(string mutation)
    {
        ManagedApiModel model = Model(withMethod: false);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string path = "Types/ManagedNativeCallbackHandler.g.cs", original = sink.sources[path];
        string changed = mutation switch
        {
            "result" => original.Replace("delegate int", "delegate long", StringComparison.Ordinal),
            "argument" => original.Replace("int argument0", "long argument0", StringComparison.Ordinal),
            "convention" => original.Replace("CallingConvention.Cdecl", "CallingConvention.StdCall", StringComparison.Ordinal),
            _ => original.Replace("public unsafe delegate", "internal unsafe delegate", StringComparison.Ordinal)
        };
        Assert.NotEqual(original, changed);
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(source => (source.Key, source.Key == path ? changed : source.Value)), "native"));
    }

    private static ManagedApiModel Model(bool withMethod)
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType callable = new(new("function"), "int (int)", NativeTypeCategory.Function, null, null)
        { declarationName = "", returnType = integer.id, parameterTypes = [integer.id], callingConvention = "C" };
        InteropType pointer = new(new("function-pointer"), "int (*)(int)", NativeTypeCategory.Pointer, 8, 8) { elementType = callable.id };
        InteropType alias = new(new("typedef"), "NativeCallback", NativeTypeCategory.Alias, 8, 8)
        { declarationName = "NativeCallback", elementType = pointer.id };
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:explicit-call-only-protocol");
        InteropFunction function = new(new("invoke"), "invoke", integer.id,
            [new(new("callback"), "callback", alias.id)
            {
                callback = new(callable.id, new(NativeLifetimeKind.Call, null, evidence), evidence)
                { retained = false, threading = NativeCallbackThreading.CallerThread, failureReturn = new(NativeScalarConstantKind.Integer, "-1", 4, true, evidence) }
            }], "C");
        InteropModule module = new("delegates", "windows-x64-msvc", 8, [integer, callable, pointer, alias], withMethod ? [function] : [], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        return new DefaultManagedProjection().Project(module, new("Generated.Delegates", "NativeApi")
        { naming = new() { callbacks = new() { prefix = "Managed", suffix = "Handler" } } }, CancellationToken.None);
    }
}
