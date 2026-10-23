using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class CallbackStatusCompositionTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, "scalar")]
    [InlineData(BindingImportMode.DllImport, "scalar")]
    [InlineData(BindingImportMode.LibraryImport, "scalar")]
    [InlineData(BindingImportMode.FunctionTable, "void")]
    [InlineData(BindingImportMode.DllImport, "void")]
    [InlineData(BindingImportMode.LibraryImport, "void")]
    [InlineData(BindingImportMode.FunctionTable, "owned")]
    [InlineData(BindingImportMode.DllImport, "owned")]
    [InlineData(BindingImportMode.LibraryImport, "owned")]
    public async Task StatusCleanupIsReusedBeforeCallbackFailureAndOwnedResultsAreReleased(BindingImportMode mode, string shape)
    {
        var model = new DefaultManagedProjection().Project(Module(shape), new("Generated.Status", "StatusApi"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpCallbackMethodPlan callback = Assert.Single(plan.callScopedCallbacks);
        Assert.NotNull(callback.statusOperation);
        Assert.Empty(plan.callbackOmissions);
        Assert.Equal(2, callback.signature.parameterTypes.Length);
        Assert.Equal(shape == "owned", callback.statusOperation.ownedResult is not null);
        Assert.Equal(shape == "void" ? "void" : shape == "scalar" ? "int" : "global::Generated.Status.StatusApi.WidgetObject", callback.signature.returnType);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string source = sink.sources.Single(item => item.Key.StartsWith("Callbacks/", StringComparison.Ordinal)).Value;
        Assert.Contains("this." + plan.methods[callback.methodIndex].name + "(", source);
        Assert.Contains("ExceptionDispatchInfo.Throw", source);
        Assert.DoesNotContain("m_function", source);
        if (shape == "owned")
        {
            Assert.Contains(".Dispose();", source);
            Assert.Contains(".releaseFailure", source);
            string changed = source.Replace(".Dispose();", ".ToString();", StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                sink.sources.Select(item => (item.Key, item.Value == source ? changed : item.Value)), "native"));
        }
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(item => (item.Key, item.Value)), "native");
    }

    private static InteropModule Module(string shape)
    {
        InteropModule module = OwnedProjectionTests.Module();
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:call-only-status-callback");
        InteropType pointer = new(new("userdata-pointer"), "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("void") };
        InteropType callable = new(new("callback"), "int (*)(int,void*)", NativeTypeCategory.Function, null, null)
        { returnType = new("int"), parameterTypes = [new("int"), pointer.id], callingConvention = "C" };
        InteropType callbackPointer = new(new("callback-pointer"), "callback*", NativeTypeCategory.Pointer, 8, 8) { elementType = callable.id };
        InteropParameter callback = new(new("callback-input"), "callback", callbackPointer.id)
        {
            callback = new(callable.id, new(NativeLifetimeKind.Call, null, evidence), evidence)
            { retained = false, threading = NativeCallbackThreading.CallerThread, userDataParameter = new("userdata"), callableUserDataIndex = 1,
                failureReturn = new(NativeScalarConstantKind.Integer, "-7", 4, true, evidence) }
        };
        InteropFunction create = module.functions[0];
        var parameters = create.parameters.Insert(0, callback).Insert(2, new(new("userdata"), "userdata", pointer.id));
        if (shape != "owned")
        {
            InteropType output = new(new("scalar-output"), "int*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("int") };
            module = new(module.libraryId, module.targetId, module.pointerSize, module.types.Add(output), module.functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
            parameters = parameters.SetItem(3, parameters[3] with { type = output.id, ownership = null });
            if (shape == "void") parameters = parameters.RemoveAt(3);
        }
        create = create with { parameters = parameters, error = create.error! with { valueParameter = shape == "void" ? null : create.error.valueParameter } };
        return new(module.libraryId, module.targetId, module.pointerSize, module.types.AddRange(ImmutableArray.Create(pointer, callable, callbackPointer)), module.functions.SetItem(0, create), []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
    }
}
