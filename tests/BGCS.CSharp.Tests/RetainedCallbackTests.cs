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

public sealed class RetainedCallbackTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task ReturnedHandleAndFullNativeFrameJoinRemainCheckedAcrossImports(BindingImportMode mode)
    {
        ManagedApiModel model = Model();
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpRetainedCallbackPlan operation = Assert.Single(plan.retainedCallbacks);
        Assert.Empty(plan.callScopedCallbacks);
        Assert.Empty(plan.callbackOmissions);
        Assert.True(operation.callback.staticThunk);
        Assert.Equal("void*", operation.handleType);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string source = sink.sources.Single(source => source.Key.Contains("Registration_", StringComparison.Ordinal)).Value;
        Assert.Contains("KeepOpenUntilUnregistered", source);
        Assert.Contains("while (!m_ready || m_nativeUses != 0)", source);
        Assert.Contains("m_session.AcquireResource()", source);
        foreach ((string before, string after) in new[] { ("KeepOpenUntilUnregistered", "CloseBeforeUnregister"), ("m_nativeUses != 0", "m_nativeUses < 0"),
            (".token.token", ".token.token + 1"), ("(int)(-7)", "(int)(-8)") })
        {
            string changed = source.Replace(before, after, StringComparison.Ordinal);
            Assert.NotEqual(source, changed);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                sink.sources.Select(item => (item.Key, item.Value == source ? changed : item.Value)), "native"));
        }
    }

    [Theory]
    [InlineData("quiescence")]
    [InlineData("null")]
    [InlineData("thread")]
    [InlineData("handle")]
    [InlineData("state")]
    [InlineData("browser")]
    public void MissingOrIncompatibleNativeGuaranteesPreserveRawWithoutOwningRegistration(string fault)
    {
        ManagedApiModel original = Model();
        InteropFunction register = original.interop.functions[0];
        CallbackContract callback = register.parameters[0].callback!;
        callback = fault switch
        {
            "quiescence" => callback with { unregisterQuiescence = NativeQuiescenceKind.NoNewInvocations },
            "null" => callback with { registration = callback.registration! with { nullResultQuiescence = NativeQuiescenceKind.Unknown } },
            "thread" => callback with { registration = callback.registration! with { releaseThreadDomain = "unknown" } },
            "handle" => callback with { registration = callback.registration! with { unregisterHandleParameter = new("not-a-handle") } },
            "state" => callback with { userDataParameter = null, callableUserDataIndex = null },
            _ => callback
        };
        InteropModule module = new(original.interop.libraryId, original.interop.targetId, fault == "browser" ? 4 : 8,
            fault == "browser" ? original.interop.types.Select(type => type.category == NativeTypeCategory.Pointer ? type with { size = 4, alignment = 4 } : type).ToImmutableArray() : original.interop.types,
            original.interop.functions.SetItem(0, register with { parameters = register.parameters.SetItem(0, register.parameters[0] with { callback = callback }) }), [])
        { byteOrder = fault == "browser" ? NativeByteOrder.LittleEndian : original.interop.byteOrder };
        EmissionRequest request = new("native", new NeutralBackendTests.MemorySink())
        {
            managedRuntime = fault == "browser" ? new("nonblocking-test", new ManagedRuntimeId("fixture.nonblocking-runtime"), NativeModuleMechanism.StaticAdapter,
                [BindingImportMode.FunctionTable], true, false) { functionPointerTransport = ManagedFunctionPointerTransport.NativeInteger } : null,
            staticExportTable = fault == "browser" ? new("retained", "retained_get", "retained_revision", "retained_count", "retained_identity", "retained_pointer_size",
                new string('a', 64), module.functions.Select(function => function.exportName).ToImmutableArray()) : null
        };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(original with { interop = module }, request);
        Assert.Empty(plan.retainedCallbacks);
        Assert.Equal(2, plan.methods.Length);
        Assert.Single(plan.callbackOmissions);
    }

    internal static ManagedApiModel Model()
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:returned-handle-full-stop-and-join");
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType empty = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropType pointer = new(new("pointer"), "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = empty.id };
        InteropType callable = new(new("callable"), "int (*)(int,void*)", NativeTypeCategory.Function, null, null)
        { returnType = integer.id, parameterTypes = [integer.id, pointer.id], callingConvention = "C" };
        InteropType callbackPointer = new(new("callback-pointer"), "Callback", NativeTypeCategory.Pointer, 8, 8) { elementType = callable.id };
        CallbackContract callback = new(callable.id, new(NativeLifetimeKind.Registration, null, evidence), evidence)
        {
            retained = true, threading = NativeCallbackThreading.AnyThread, userDataParameter = new("userdata"), callableUserDataIndex = 1,
            failureReturn = new(NativeScalarConstantKind.Integer, "-7", 4, true, evidence), unregisterFunction = new("stop"),
            unregisterQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations,
            registration = new(new("handle"), NativeQuiescenceKind.NoActiveNativeInvocations, "any", evidence)
        };
        InteropFunction register = new(new("register"), "register", pointer.id,
            [new(new("callback"), "callback", callbackPointer.id) { callback = callback }, new(new("userdata"), "userdata", pointer.id)], "C");
        InteropFunction stop = new(new("stop"), "stop", empty.id, [new(new("handle"), "handle", pointer.id)], "C");
        InteropModule module = new("retained", "windows-x64-msvc", 8, [integer, empty, pointer, callable, callbackPointer], [register, stop], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        return new DefaultManagedProjection().Project(module, new("Generated.Retained", "CallbackApi"), CancellationToken.None);
    }
}
