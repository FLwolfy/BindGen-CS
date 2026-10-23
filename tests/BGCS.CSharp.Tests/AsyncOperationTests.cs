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
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class AsyncOperationTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task TerminalSignalSchedulesFullNativeJoinBeforeMemoryPinsAndTaskCompletion(BindingImportMode mode)
    {
        ManagedApiModel model = Model();
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode, includeRuntimeSource = true };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpAsyncOperationPlan operation = Assert.Single(plan.asyncOperations);
        Assert.Empty(plan.callbackOmissions);
        Assert.Empty(plan.callScopedCallbacks);
        Assert.Empty(plan.retainedCallbacks);
        Assert.Equal("global::System.Memory<int>", Assert.Single(operation.signature.parameterTypes));
        Assert.Equal("global::BGCS.Runtime.NativeOperation<int>", operation.signature.returnType);
        Assert.Single(operation.buffers);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        Assert.Contains(sink.sources.Keys, path => path.EndsWith("NativeOperation.cs", StringComparison.Ordinal));
        string source = sink.sources.Single(item => item.Key.StartsWith("Async/", StringComparison.Ordinal)).Value;
        Assert.Contains("Task.Run(Finish)", source);
        Assert.Contains("registration!.Dispose()", source);
        Assert.Contains("TrySetCanceled()", source);
        foreach ((string before, string after) in new[] { ("Task.Run(Finish)", "Task.Run(Cancel)"), ("registration!.Dispose()", "registration!.GetType()"),
            ("pin2.Dispose()", "pin2.GetHashCode()"), ("m_cancelCalls != 0", "m_cancelCalls < 0") })
        {
            string changed = source.Replace(before, after, StringComparison.Ordinal);
            Assert.NotEqual(source, changed);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                sink.sources.Select(item => (item.Key, item.Value == source ? changed : item.Value)), "native"));
        }
    }

    [Theory]
    [InlineData("completion")]
    [InlineData("retention")]
    [InlineData("buffer-lifetime")]
    [InlineData("missing-pin")]
    [InlineData("status-width")]
    [InlineData("same-status")]
    [InlineData("same-channel")]
    [InlineData("negative-zero")]
    [InlineData("duplicate-retention")]
    [InlineData("undefined-guarantee")]
    public void UnknownTerminalOrRetainedStorageProtocolsKeepOnlyTheExactRawAbi(string fault)
    {
        ManagedApiModel original = Model();
        InteropFunction start = original.interop.functions[0];
        AsyncContract protocol = start.asynchronous!;
        if (fault == "buffer-lifetime") start = start with
        {
            parameters = start.parameters.SetItem(2, start.parameters[2] with
            { buffer = start.parameters[2].buffer! with { lifetime = start.parameters[2].buffer!.lifetime with { kind = NativeLifetimeKind.Unknown } } })
        };
        protocol = fault switch
        {
            "completion" => protocol with { completionQuiescence = NativeQuiescenceKind.Unknown },
            "retention" => protocol with { retainedParameters = default },
            "missing-pin" => protocol with { retainedParameters = [] },
            "status-width" => protocol with { successStatus = protocol.successStatus! with { width = 8 } },
            "same-status" => protocol with { cancellationStatus = protocol.successStatus },
            "same-channel" => protocol with { statusArgumentIndex = protocol.resultArgumentIndex },
            "negative-zero" => protocol with { cancellationStatus = protocol.cancellationStatus! with { value = "-0" } },
            "duplicate-retention" => protocol with { retainedParameters = [new("memory"), new("memory")] },
            "undefined-guarantee" => protocol with { completionQuiescence = (NativeQuiescenceKind)999 },
            _ => protocol
        };
        InteropModule module = new(original.interop.libraryId, original.interop.targetId, 8, original.interop.types,
            original.interop.functions.SetItem(0, start with { asynchronous = protocol }), [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(original with { interop = module }, new("native", new NeutralBackendTests.MemorySink()));
        Assert.Empty(plan.asyncOperations);
        Assert.Single(plan.callbackOmissions);
        Assert.Equal(3, plan.methods.Length);
    }

    internal static ManagedApiModel Model()
    {
        ManagedApiModel original = RetainedCallbackTests.Model();
        InteropModule module = original.interop;
        Evidence evidence = module.functions[0].parameters[0].callback!.evidence;
        InteropType pointer = module.GetType(new("pointer")), integer = module.GetType(new("int"));
        InteropType callable = module.GetType(new("callable")) with { returnType = new("void"), parameterTypes = [integer.id, integer.id, pointer.id] };
        InteropType buffer = new(new("int-pointer"), "int*", NativeTypeCategory.Pointer, 8, 8) { elementType = integer.id };
        InteropFunction start = module.functions[0];
        CallbackContract callback = start.parameters[0].callback! with
        { lifetime = new(NativeLifetimeKind.Operation, null, evidence), callableUserDataIndex = 2, failureReturn = null };
        start = start with
        {
            parameters = start.parameters.SetItem(0, start.parameters[0] with { callback = callback })
                .Add(new(new("memory"), "memory", buffer.id)
                {
                    buffer = new(integer.id, NativeLengthUnit.Elements, NativeParameterDirection.InputOutput,
                    new(NativeLifetimeKind.Operation, null, evidence), evidence)
                    { lengthParameter = new("length"), retainsPointer = true, allowsNull = false }
                })
                .Add(new(new("length"), "length", integer.id)),
            asynchronous = new(new("callback"), NativeQuiescenceKind.NoNewInvocations, evidence)
            {
                resultArgumentIndex = 0,
                statusArgumentIndex = 1,
                successStatus = new(NativeScalarConstantKind.Integer, "0", 4, true, evidence),
                cancellationStatus = new(NativeScalarConstantKind.Integer, "1", 4, true, evidence),
                cancelFunction = new("cancel"),
                cancellationHandleParameter = new("cancel-handle"),
                retainedParameters = [new("memory")]
            }
        };
        InteropFunction cancel = new(new("cancel"), "cancel", new("void"), [new(new("cancel-handle"), "handle", pointer.id)], "C");
        InteropModule updated = new(module.libraryId, module.targetId, 8, module.types.Select(type => type.id == callable.id ? callable : type).Append(buffer).ToImmutableArray(),
            [start, module.functions[1], cancel], [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        return new DefaultManagedProjection().Project(updated, new("Generated.Async", "AsyncApi"), CancellationToken.None);
    }
}
