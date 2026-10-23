using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
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

public sealed class OwnedInstanceCallbackTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ExactOwnedReceiverUsesOneRootedAddressAndCommonOwnedResultCleanup(
        BindingImportMode mode,
        bool userData
    ) {
        ManagedApiModel model = Model(userData);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        Assert.Equal(2, plan.callScopedCallbacks.Length);
        Assert.Empty(plan.callbackOmissions);
        CSharpObjectPlan owner = Assert.Single(plan.objects);
        foreach (CSharpCallbackMethodPlan operation in plan.callScopedCallbacks)
        {
            CSharpCallbackReceiverPlan receiver = Assert.IsType<CSharpCallbackReceiverPlan>(operation.receiver);
            Assert.Equal(owner.name, receiver.objectName);
            Assert.Equal(0, receiver.instanceIndex);
            Assert.Equal(operation.signature.parameterTypes.Length - 1, receiver.objectSignature.parameterTypes.Length);
            Assert.Contains("WidgetObject", operation.signature.parameterTypes[0]);
            Assert.Equal(userData, Assert.Single(operation.callbacks).staticThunk);
        }
        Assert.Contains(plan.statusMethods, status => status.instanceIndex == 0 && status.ownedResult is not null
            && plan.methods[status.methodIndex].nativeExport == new StableId("clone"));
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string callbacks = string.Join("\n", sink.sources.Where(source => source.Key.StartsWith("Callbacks/", StringComparison.Ordinal)).Select(source => source.Value));
        Assert.Contains(".__BgcsBelongsTo(this)", callbacks);
        Assert.Contains(".Invoke(__bgcsCallbackReceiverAddress =>", callbacks);
        Assert.Contains(".Dispose();", callbacks);
        Assert.Contains(".releaseFailure", callbacks);
        Assert.Equal(userData ? 0 : 2, callbacks.Split("GetFunctionPointerForDelegate(", StringSplitOptions.None).Length - 1);
        if (userData) Assert.Equal(2, callbacks.Split("nint __bgcsCallbackPointer", StringSplitOptions.None).Length - 1);
        string owned = Assert.Single(sink.sources, source => source.Value.Contains("private WidgetObject __BgcsObjectCall", StringComparison.Ordinal)).Value;
        Assert.Contains("m_handle.Invoke(", owned);
        Assert.Contains("AcquireResource()", owned);
        Assert.Contains("new WidgetObject(this,", owned);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), "native");
        foreach (string mutation in new[] { "lease", "root", "duplicate-pointer", "object-forwarder", "resource", "error-release" })
        {
            var changed = sink.sources.Select(source => (source.Key, mutation switch
            {
                "lease" => source.Value.Replace(".Invoke(__bgcsCallbackReceiverAddress =>", ".ToString(__bgcsCallbackReceiverAddress =>", StringComparison.Ordinal),
                "root" => source.Value.Replace("nint __bgcsCallbackPointer", "nuint __bgcsCallbackPointer", StringComparison.Ordinal),
                "duplicate-pointer" => Regex.Replace(source.Value, @"(?m)^ +nint __bgcsCallbackPointer[^=\r\n]+ = ([^;\r\n]+);",
                    match => match.Value + "\n        _ = " + match.Groups[1].Value + ";"),
                "object-forwarder" => source.Value.Replace("this.m_context.Run(", "this.m_context.Spawn(", StringComparison.Ordinal),
                "error-release" => source.Value.Replace("if (__bgcsError != null)", "if (false)", StringComparison.Ordinal),
                _ => source.Value.Replace("AcquireResource()", "AcquireCall()", StringComparison.Ordinal)
            })).ToArray();
            Assert.Contains(changed, source => sink.sources[source.Key] != source.Item2);
            Exception? failure = Record.Exception(() => new EmittedInteropContractReader().Verify(plan, changed, "native"));
            Assert.True(failure is InvalidOperationException, "The final contract must reject mutation '" + mutation + "': " + failure);
        }
    }

    [Theory]
    [InlineData("retained-self")]
    [InlineData("unknown-call")]
    [InlineData("wrong-owner")]
    [InlineData("const-factory")]
    public void UnprovedReceiverDoesNotGainAnAutomaticCallbackAddress(string fault)
    {
        ManagedApiModel model = Model(true);
        InteropModule original = model.interop;
        InteropFunction run = original.functions.Single(function => function.id == new StableId("run"));
        InteropParameter self = run.parameters[0];
        if (fault == "wrong-owner") run = run with { sourceOwnerType = new("different-owner") };
        else if (fault != "const-factory")
            run = run with
            {
                parameters = run.parameters.SetItem(0, self with
                {
                    ownership = self.ownership! with
                    {
                        retainsPointer = fault == "retained-self" ? true : null,
                        lifetime = fault == "unknown-call" ? self.ownership.lifetime with { kind = NativeLifetimeKind.Unknown } : self.ownership.lifetime
                    }
                })
            };
        ImmutableArray<InteropType> types = original.types;
        var functions = original.functions.SetItem(original.functions.IndexOf(original.functions.Single(function => function.id == run.id)), run);
        if (fault == "const-factory")
        {
            InteropType qualified = new(new("const-widget"), "const widget", NativeTypeCategory.Qualified, null, null) { elementType = new("widget"), qualifier = "Const" };
            InteropType pointer = new(new("const-widget-pointer"), "const widget*", NativeTypeCategory.Pointer, 8, 8) { elementType = qualified.id };
            InteropType output = new(new("const-widget-output"), "const widget**", NativeTypeCategory.Pointer, 8, 8) { elementType = pointer.id };
            types = types.AddRange(ImmutableArray.Create(qualified, pointer, output));
            InteropFunction create = functions[0];
            functions = functions.SetItem(0, create with { parameters = create.parameters.SetItem(1, create.parameters[1] with { type = output.id }) });
            InteropFunction release = functions[1];
            functions = functions.SetItem(1, release with { parameters = release.parameters.SetItem(0, release.parameters[0] with { type = pointer.id }) });
        }
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, types, functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel changed = new DefaultManagedProjection().Project(module, new(model.@namespace, model.name), CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(changed, new("native", new NeutralBackendTests.MemorySink()));
        Assert.DoesNotContain(plan.callScopedCallbacks, operation => plan.methods[operation.methodIndex].nativeExport == run.id);
        Assert.Contains(plan.callbackOmissions, omission => omission.nativeExport == run.id);
        Assert.Contains(plan.methods, method => method.nativeExport == run.id);
    }

    [Theory]
    [InlineData("fixture.static-runtime")]
    [InlineData("fixture.nonblocking-runtime")]
    public void StaticReverseCallCapabilityStillRequiresExplicitUserData(string runtime)
    {
        ManagedApiModel Prepare(bool userData)
        {
            ManagedApiModel model = Model(userData);
            return runtime != "fixture.nonblocking-runtime" ? model : model with
            {
                interop = new(model.interop.libraryId, model.interop.targetId, 4,
                    model.interop.types.Select(type => type.category == NativeTypeCategory.Pointer ? type with { size = 4, alignment = 4 } : type).ToImmutableArray(),
                    model.interop.functions, [])
                { byteOrder = NativeByteOrder.LittleEndian }
            };
        }
        ManagedApiModel initial = Prepare(false);
        EmissionRequest request = new("native", new NeutralBackendTests.MemorySink())
        {
            managedRuntime = new("static-reverse", new(runtime),
                runtime == "fixture.nonblocking-runtime" ? NativeModuleMechanism.StaticAdapter : NativeModuleMechanism.ExternalFunctionTable,
                [BindingImportMode.FunctionTable], true, false)
            { functionPointerTransport = runtime == "fixture.nonblocking-runtime" ? ManagedFunctionPointerTransport.NativeInteger : ManagedFunctionPointerTransport.Typed },
            staticExportTable = runtime == "fixture.nonblocking-runtime" ? new("owned_callbacks", "owned_callbacks_get", "owned_callbacks_revision", "owned_callbacks_count",
                "owned_callbacks_identity", "owned_callbacks_pointer_size", new string('a', 64), initial.interop.functions.Select(function => function.exportName).ToImmutableArray()) : null
        };
        CSharpEmissionPlan without = new CSharpTestPlanner().Plan(initial, request);
        Assert.Empty(without.callScopedCallbacks);
        Assert.Equal(2, without.callbackOmissions.Length);
        CSharpEmissionPlan withState = new CSharpTestPlanner().Plan(Prepare(true), request);
        Assert.Equal(2, withState.callScopedCallbacks.Length);
        Assert.All(withState.callScopedCallbacks, operation => Assert.True(Assert.Single(operation.callbacks).staticThunk));
    }

    [Fact]
    public void ModuleBorrowedMemberMetadataCannotAcquireTheOwnedCallbackRoute()
    {
        ManagedApiModel original = Model(true);
        ManagedMember run = original.methods.Single(member => member.nativeExport == new StableId("run"));
        ManagedApiModel model = original with
        {
            methods = original.methods.SetItem(original.methods.IndexOf(run), run with
            {
                receiver = new(new("widget"), new("run-self"), "BorrowedWidget") { kind = ManagedReceiverKind.ModuleBorrowed }
            })
        };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()));
        Assert.DoesNotContain(plan.callScopedCallbacks, operation => plan.methods[operation.methodIndex].nativeExport == run.nativeExport);
        Assert.Contains(plan.callbackOmissions, omission => omission.nativeExport == run.nativeExport && omission.reason.Contains("module-borrowed", StringComparison.Ordinal));
        Assert.Contains(plan.methods, method => method.nativeExport == run.nativeExport);
    }

    internal static ManagedApiModel Model(bool userData)
    {
        InteropModule original = OwnedProjectionTests.Module();
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:owned-native-call-only");
        InteropType state = new(new("state"), "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("void") };
        InteropType callable = new(new("callback"), "int (*)(int,void*)", NativeTypeCategory.Function, null, null)
        { returnType = new("int"), parameterTypes = userData ? [new("int"), state.id] : [new("int")], callingConvention = "C" };
        InteropType callbackPointer = new(new("callback-pointer"), "callback*", NativeTypeCategory.Pointer, 8, 8) { elementType = callable.id };
        InteropType integerOutput = new(new("integer-output"), "int*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("int") };
        InteropFunction create = original.functions[0];
        InteropFunction Member(
            string name,
            bool owned,
            bool hasCallback
        ) {
            StableId resultId = new(name + "-result"), errorId = new(name + "-error");
            OwnershipContract errorOwner = create.parameters[2].ownership! with { lifetime = create.parameters[2].ownership!.lifetime with { owner = errorId } };
            InteropParameter error = create.parameters[2] with { id = errorId, ownership = errorOwner };
            InteropParameter result = create.parameters[1] with
            {
                id = resultId,
                type = owned ? create.parameters[1].type : integerOutput.id,
                ownership = owned ? create.parameters[1].ownership! with { lifetime = create.parameters[1].ownership!.lifetime with { owner = resultId } } : null
            };
            var inputs = ImmutableArray.CreateBuilder<InteropParameter>();
            inputs.Add(new(new(name + "-self"), "self", new("widget-pointer"))
            { ownership = new(NativeOwnershipKind.Borrowed, new(NativeLifetimeKind.Call, null, evidence), evidence) });
            if (hasCallback) inputs.Add(new(new(name + "-callback"), "callback", callbackPointer.id)
            {
                callback = new(callable.id, new(NativeLifetimeKind.Call, null, evidence), evidence)
                {
                    retained = false,
                    threading = NativeCallbackThreading.CallerThread,
                    userDataParameter = userData ? new(name + "-userdata") : null,
                    callableUserDataIndex = userData ? 1 : null,
                    failureReturn = new(NativeScalarConstantKind.Integer, "-7", 4, true, evidence)
                }
            });
            inputs.Add(new(new(name + "-value"), "value", new("int")));
            if (hasCallback && userData) inputs.Add(new(new(name + "-userdata"), "userdata", state.id));
            inputs.Add(result); inputs.Add(error);
            return new(new(name), name, new("int"), inputs.ToImmutable(), "C")
            { sourceOwnerType = create.sourceOwnerType, error = new("0", evidence) { valueParameter = resultId, errorParameter = errorId, errorOwnership = errorOwner } };
        }
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize,
            original.types.AddRange(ImmutableArray.Create(state, callable, callbackPointer, integerOutput)),
            original.functions.AddRange(ImmutableArray.Create(Member("run", false, true), Member("spawn", true, true), Member("clone", true, false))), [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        return new DefaultManagedProjection().Project(module, new("Generated.OwnedCallbacks", "WidgetApi"), CancellationToken.None);
    }
}
