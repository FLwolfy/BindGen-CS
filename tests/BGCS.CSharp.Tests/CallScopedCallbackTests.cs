using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class CallScopedCallbackTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task FixedReverseAbiAndStateLifetimeRemainCheckedForEveryImport(BindingImportMode mode, bool userData)
    {
        ManagedApiModel model = Model(userData);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpCallbackMethodPlan operation = Assert.Single(plan.callScopedCallbacks);
        CSharpCallbackInputPlan callback = Assert.Single(operation.callbacks);
        Assert.Equal(userData, callback.staticThunk);
        Assert.Equal("(int)(-7)", callback.fallbackExpression);
        Assert.Equal(2, operation.signature.parameterTypes.Length);
        Assert.Empty(plan.callbackOmissions);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string text = sink.sources.Single(source => source.Key.StartsWith("Callbacks/", StringComparison.Ordinal)).Value;
        Assert.Contains("ExceptionDispatchInfo.Throw", text);
        Assert.Contains("m_session.AcquireCall()", text);
        if (userData)
        {
            Assert.Contains("UnmanagedCallersOnly", text);
            Assert.Contains("CallbackRegistry<", text);
            Assert.DoesNotContain("GetFunctionPointerForDelegate", text);
        }
        else
        {
            Assert.Contains("UnmanagedFunctionPointer", text);
            Assert.Contains("GC.KeepAlive", text);
        }
        foreach (string mutation in new[] { "fallback", "capture", "lease", "root" })
        {
            string changed = mutation switch
            {
                "fallback" => text.Replace("(int)(-7)", "(int)(-8)", StringComparison.Ordinal),
                "capture" => text.Replace("CompareExchange", "Exchange", StringComparison.Ordinal),
                "lease" => text.Replace("AcquireCall()", "AcquireResource()", StringComparison.Ordinal),
                _ => userData ? text.Replace(".token", ".token + 1", StringComparison.Ordinal)
                    : text.Replace("GC.KeepAlive", "GC.SuppressFinalize", StringComparison.Ordinal)
            };
            Assert.NotEqual(text, changed);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                sink.sources.Select(source => (source.Key, source.Value == text ? changed : source.Value)), "native"));
        }
    }

    [Theory]
    [InlineData("lifetime")]
    [InlineData("retained")]
    [InlineData("threading")]
    [InlineData("fallback")]
    [InlineData("width")]
    [InlineData("aot")]
    public void UnknownOrIncompatibleContractsPreserveRawAndExplainTheMissingManagedWrapper(string missing)
    {
        ManagedApiModel model = Model(false);
        InteropFunction function = Assert.Single(model.interop.functions);
        InteropParameter parameter = function.parameters[0];
        CallbackContract contract = parameter.callback!;
        contract = missing switch
        {
            "lifetime" => contract with { lifetime = contract.lifetime with { kind = NativeLifetimeKind.Unknown } },
            "retained" => contract with { retained = true },
            "threading" => contract with { threading = NativeCallbackThreading.Unknown },
            "fallback" => contract with { failureReturn = null },
            "width" => contract with { failureReturn = contract.failureReturn! with { width = 8 } },
            _ => contract
        };
        InteropModule interop = new(model.interop.libraryId, model.interop.targetId, 8, model.interop.types,
            [function with { parameters = function.parameters.SetItem(0, parameter with { callback = contract }) }], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        EmissionRequest request = new("native", new NeutralBackendTests.MemorySink())
        {
            managedRuntime = missing == "aot" ? new("native-aot", new ManagedRuntimeId("fixture.static-runtime"), NativeModuleMechanism.DynamicLibrary,
                [BindingImportMode.FunctionTable], true, true) : null
        };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model with { interop = interop }, request);
        Assert.Empty(plan.callScopedCallbacks);
        Assert.Single(plan.methods);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(plan.callbackOmissions).reason));
    }

    [Fact]
    public void OtherPointerInputsRetainTheirExactUnsafeCarrierWithoutInventingStorageSemantics()
    {
        ManagedApiModel original = Model(true);
        InteropFunction function = original.interop.functions[0];
        InteropType pointer = original.interop.types.Single(type => type.id == new StableId("state"));
        function = function with { parameters = function.parameters.Insert(2, new(new("raw-pointer"), "rawPointer", pointer.id)) };
        InteropModule module = new(original.interop.libraryId, original.interop.targetId, 8, original.interop.types, [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Callbacks", "CallbackApi"), CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()));
        CSharpCallbackMethodPlan operation = Assert.Single(plan.callScopedCallbacks);
        Assert.Equal("void*", operation.signature.parameterTypes[2]);
        Assert.Equal(3, Assert.Single(operation.callbacks).userDataIndex);
        Assert.Null(function.parameters[2].ownership);
        Assert.Null(function.parameters[2].singleValue);
        Assert.Null(function.parameters[2].buffer);
    }

    private static ManagedApiModel Model(bool userData)
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:call-only-no-retention-and-threads");
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType empty = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropType state = new(new("state"), "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = empty.id };
        InteropType callable = new(new("callable"), "int (*)(int)", NativeTypeCategory.Function, null, null)
        { returnType = integer.id, parameterTypes = userData ? [integer.id, state.id] : [integer.id], callingConvention = "C" };
        InteropType pointer = new(new("callback-pointer"), "Callback", NativeTypeCategory.Pointer, 8, 8) { elementType = callable.id };
        CallbackContract contract = new(callable.id, new(NativeLifetimeKind.Call, null, evidence), evidence)
        {
            retained = false, threading = NativeCallbackThreading.CallerThread,
            userDataParameter = userData ? new StableId("userdata") : null, callableUserDataIndex = userData ? 1 : null,
            failureReturn = new(NativeScalarConstantKind.Integer, "-7", 4, true, evidence)
        };
        ImmutableArray<InteropParameter> parameters = [new(new("callback"), "callback", pointer.id) { callback = contract }, new(new("value"), "value", integer.id)];
        if (userData) parameters = parameters.Add(new(new("userdata"), "userdata", state.id));
        InteropFunction function = new(new("invoke"), "invoke", integer.id, parameters, "C");
        InteropModule module = new("callbacks", "windows-x64-msvc", 8, [integer, empty, state, callable, pointer], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        return new DefaultManagedProjection().Project(module, new("Generated.Callbacks", "CallbackApi"), CancellationToken.None);
    }
}
