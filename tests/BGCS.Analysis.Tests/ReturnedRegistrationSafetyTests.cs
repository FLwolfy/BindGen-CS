using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Safety;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Analysis.Tests;

public sealed class ReturnedRegistrationSafetyTests
{
    [Theory]
    [InlineData(false, EvidenceLevel.Declared, NativeCallbackThreading.AnyThread)]
    [InlineData(false, EvidenceLevel.UserAsserted, NativeCallbackThreading.CallerThread)]
    [InlineData(false, EvidenceLevel.Verified, NativeCallbackThreading.AnyThread)]
    [InlineData(true, EvidenceLevel.Declared, NativeCallbackThreading.AnyThread)]
    [InlineData(true, EvidenceLevel.UserAsserted, NativeCallbackThreading.AnyThread)]
    [InlineData(true, EvidenceLevel.Verified, NativeCallbackThreading.CallerThread)]
    [InlineData(false, EvidenceLevel.UserAsserted, NativeCallbackThreading.EventLoop)]
    [InlineData(true, EvidenceLevel.UserAsserted, NativeCallbackThreading.EventLoop)]
    public void ExactTerminalRegistrationFactsDoNotRequireASecondPointerCleanup(bool asynchronous, EvidenceLevel level, NativeCallbackThreading threading)
    {
        InteropModule module = Module(asynchronous, level, threading);
        InteropFunction original = module.functions[0];
        CallbackContract callback = original.parameters[0].callback!;
        Assert.Empty(new SafetyAnalyzer().Analyze(module));
        Assert.Same(original, module.functions[0]);
        Assert.Same(callback, module.functions[0].parameters[0].callback);
        Assert.Equal(level, callback.evidence.level);
        Assert.Equal(level, callback.registration!.evidence.level);
        Assert.Null(original.returnOwnership);
        Assert.Null(original.returnString);
    }

    [Theory]
    [InlineData("missing-registration")]
    [InlineData("callback-state-kind")]
    [InlineData("not-retained")]
    [InlineData("unknown-callback-evidence")]
    [InlineData("inferred-registration-evidence")]
    [InlineData("empty-lifetime-origin")]
    [InlineData("unknown-lifetime")]
    [InlineData("operation-without-async")]
    [InlineData("unknown-thread")]
    [InlineData("unknown-cleanup-domain")]
    [InlineData("wrong-thread-domain")]
    [InlineData("no-unregister")]
    [InlineData("missing-unregister-export")]
    [InlineData("self-unregister")]
    [InlineData("no-active-guarantee")]
    [InlineData("only-no-new-guarantee")]
    [InlineData("unknown-null-failure")]
    [InlineData("only-no-new-null-failure")]
    [InlineData("wrong-handle-identity")]
    [InlineData("same-size-different-pointer")]
    [InlineData("stop-not-void")]
    [InlineData("stop-variadic")]
    [InlineData("stop-no-prototype")]
    [InlineData("wrong-callable-identity")]
    [InlineData("missing-userdata")]
    [InlineData("wrong-userdata-index")]
    [InlineData("callback-no-prototype")]
    [InlineData("competing-ownership")]
    public void MissingOrContradictoryRegistrationFactsKeepAnActionableProtocolWarning(string fault)
    {
        InteropModule original = Module();
        InteropFunction function = original.functions[0];
        InteropFunction stop = original.functions[1];
        CallbackContract callback = function.parameters[0].callback!;
        CallbackRegistrationContract registration = callback.registration!;
        callback = fault switch
        {
            "missing-registration" => callback with { registration = null },
            "callback-state-kind" => callback with { registration = registration with { kind = NativeCallbackRegistrationKind.CallbackAndUserData } },
            "not-retained" => callback with { retained = false },
            "unknown-callback-evidence" => callback with { evidence = callback.evidence with { level = EvidenceLevel.Unknown } },
            "inferred-registration-evidence" => callback with { registration = registration with { evidence = registration.evidence with { level = EvidenceLevel.Inferred } } },
            "empty-lifetime-origin" => callback with { lifetime = callback.lifetime with { evidence = callback.lifetime.evidence with { origin = " " } } },
            "unknown-lifetime" => callback with { lifetime = callback.lifetime with { kind = NativeLifetimeKind.Unknown } },
            "operation-without-async" => callback with { lifetime = callback.lifetime with { kind = NativeLifetimeKind.Operation } },
            "unknown-thread" => callback with { threading = NativeCallbackThreading.Unknown },
            "unknown-cleanup-domain" => callback with { registration = registration with { releaseThreadDomain = "unknown" } },
            "wrong-thread-domain" => callback with { registration = registration with { releaseThreadDomain = "eventLoop" } },
            "no-unregister" => callback with { unregisterFunction = null },
            "missing-unregister-export" => callback with { unregisterFunction = new("missing") },
            "self-unregister" => callback with { unregisterFunction = function.id },
            "no-active-guarantee" => callback with { unregisterQuiescence = NativeQuiescenceKind.Unknown },
            "only-no-new-guarantee" => callback with { unregisterQuiescence = NativeQuiescenceKind.NoNewInvocations },
            "unknown-null-failure" => callback with { registration = registration with { nullResultQuiescence = NativeQuiescenceKind.Unknown } },
            "only-no-new-null-failure" => callback with { registration = registration with { nullResultQuiescence = NativeQuiescenceKind.NoNewInvocations } },
            "wrong-handle-identity" => callback with { registration = registration with { unregisterHandleParameter = new("other-handle") } },
            "wrong-callable-identity" => callback with { callableType = new("integer") },
            "missing-userdata" => callback with { userDataParameter = null },
            "wrong-userdata-index" => callback with { callableUserDataIndex = 7 },
            _ => callback
        };
        stop = fault switch
        {
            "same-size-different-pointer" => stop with { parameters = [stop.parameters[0] with { type = new("integer-pointer") }] },
            "stop-not-void" => stop with { returnType = new("integer") },
            "stop-variadic" => stop with { isVariadic = true },
            "stop-no-prototype" => stop with { hasPrototype = false },
            _ => stop
        };
        function = function with { parameters = function.parameters.SetItem(0, function.parameters[0] with { callback = callback }) };
        if (fault == "competing-ownership") function = function with { returnOwnership = new(NativeOwnershipKind.Unknown, callback.lifetime, callback.evidence) };
        ImmutableArray<InteropType> types = original.types;
        if (fault == "callback-no-prototype") types = types.SetItem(6, types[6] with { hasPrototype = false });
        ImmutableArray<BindingDiagnostic> diagnostics = new SafetyAnalyzer().Analyze(Copy(original, [function, stop], types));
        string[] expectedCodes = fault switch
        {
            "unknown-callback-evidence" => [BindingDiagnosticCodes.C_OWNERSHIP, BindingDiagnosticCodes.C_CALLBACKLIFETIME, BindingDiagnosticCodes.C_CALLBACKTHREADING],
            "empty-lifetime-origin" or "unknown-lifetime" => [BindingDiagnosticCodes.C_OWNERSHIP, BindingDiagnosticCodes.C_CALLBACKLIFETIME],
            "unknown-thread" => [BindingDiagnosticCodes.C_OWNERSHIP, BindingDiagnosticCodes.C_CALLBACKTHREADING],
            _ => [BindingDiagnosticCodes.C_OWNERSHIP]
        };
        Assert.Equal(expectedCodes.Order(StringComparer.Ordinal), diagnostics.Select(diagnostic => diagnostic.code).Order(StringComparer.Ordinal));
        BindingDiagnostic warning = Assert.Single(diagnostics, diagnostic => diagnostic.code == BindingDiagnosticCodes.C_OWNERSHIP);
        Assert.Equal(BindingDiagnosticCodes.C_OWNERSHIP, warning.code);
        Assert.Equal(BindingDiagnosticSeverity.Warning, warning.severity);
        Assert.Equal(function.source, warning.location);
        Assert.Contains("semantics.callbackParameters", warning.message, StringComparison.Ordinal);
        Assert.Contains("terminal join", warning.message, StringComparison.Ordinal);
        Assert.DoesNotContain("Declare semantics.ownedResults", warning.message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown-evidence")]
    [InlineData("inferred-evidence")]
    [InlineData("wrong-completion")]
    [InlineData("unknown-completion")]
    [InlineData("undefined-completion")]
    [InlineData("registration-lifetime")]
    [InlineData("missing-retained-set")]
    [InlineData("unknown-retained-parameter")]
    [InlineData("duplicate-retention")]
    [InlineData("buffer-call-lifetime")]
    [InlineData("buffer-not-retained")]
    [InlineData("cancel-missing-export")]
    [InlineData("cancel-wrong-input")]
    [InlineData("cancel-self")]
    [InlineData("cancel-is-join")]
    [InlineData("undefined-cancellation")]
    [InlineData("unknown-event-loop-stop")]
    [InlineData("unknown-event-loop-cancel")]
    public void AsyncResultMustKeepItsActualCompletionAndTerminalJoinProtocol(string fault)
    {
        bool eventLoop = fault.StartsWith("unknown-event-loop", StringComparison.Ordinal);
        InteropModule module = Module(true, threading: eventLoop ? NativeCallbackThreading.EventLoop : NativeCallbackThreading.AnyThread);
        InteropFunction start = module.functions[0];
        CallbackContract callback = start.parameters[0].callback!;
        AsyncContract asynchronous = start.asynchronous!;
        asynchronous = fault switch
        {
            "unknown-evidence" => asynchronous with { evidence = asynchronous.evidence with { level = EvidenceLevel.Unknown } },
            "inferred-evidence" => asynchronous with { evidence = asynchronous.evidence with { level = EvidenceLevel.Inferred } },
            "wrong-completion" => asynchronous with { completionCallback = new("other-callback") },
            "unknown-completion" => asynchronous with { completionQuiescence = NativeQuiescenceKind.Unknown },
            "undefined-completion" => asynchronous with { completionQuiescence = (NativeQuiescenceKind)999 },
            "missing-retained-set" => asynchronous with { retainedParameters = default },
            "unknown-retained-parameter" => asynchronous with { retainedParameters = [new("missing")] },
            "duplicate-retention" => asynchronous with { retainedParameters = [new("memory"), new("memory")] },
            "cancel-missing-export" => asynchronous with { cancelFunction = new("absent") },
            "cancel-wrong-input" => asynchronous with { cancellationHandleParameter = new("other") },
            "cancel-self" => asynchronous with { cancelFunction = start.id },
            "cancel-is-join" => asynchronous with { cancelFunction = module.functions[1].id },
            "undefined-cancellation" => asynchronous with { cancellationQuiescence = (NativeQuiescenceKind)999 },
            "unknown-event-loop-cancel" => asynchronous with { cancellationIsNonBlocking = null },
            _ => asynchronous
        };
        if (fault == "registration-lifetime") callback = callback with { lifetime = callback.lifetime with { kind = NativeLifetimeKind.Registration } };
        if (fault == "unknown-event-loop-stop") callback = callback with { registration = callback.registration! with { unregisterIsNonBlocking = null } };
        start = start with { asynchronous = asynchronous, parameters = start.parameters.SetItem(0, start.parameters[0] with { callback = callback }) };
        if (fault is "buffer-call-lifetime" or "buffer-not-retained")
        {
            BufferContract buffer = start.parameters[2].buffer!;
            buffer = fault == "buffer-call-lifetime" ? buffer with { lifetime = buffer.lifetime with { kind = NativeLifetimeKind.Call } } : buffer with { retainsPointer = false };
            start = start with { parameters = start.parameters.SetItem(2, start.parameters[2] with { buffer = buffer }) };
        }
        BindingDiagnostic warning = Assert.Single(new SafetyAnalyzer().Analyze(Copy(module, module.functions.SetItem(0, start))));
        Assert.Contains("semantics.asyncOperations", warning.message, StringComparison.Ordinal);
        Assert.Equal(start.source, warning.location);
        Assert.Null(start.returnOwnership);
    }

    [Fact]
    public void AsyncCompletionMayPrecedeTheNativeFrameEndBecauseTheIndependentJoinIsStillRequired()
    {
        InteropModule module = Module(true);
        Assert.Equal(NativeQuiescenceKind.NoNewInvocations, module.functions[0].asynchronous!.completionQuiescence);
        Assert.Empty(new SafetyAnalyzer().Analyze(module));
        CallbackContract callback = module.functions[0].parameters[0].callback!;
        InteropFunction start = module.functions[0] with { parameters = module.functions[0].parameters.SetItem(0,
            module.functions[0].parameters[0] with { callback = callback with { unregisterQuiescence = NativeQuiescenceKind.NoNewInvocations } }) };
        Assert.Single(new SafetyAnalyzer().Analyze(Copy(module, module.functions.SetItem(0, start))));
    }

    [Fact]
    public void MalformedStopRepresentationCannotSilentlySuppressTheWarning()
    {
        InteropModule module = Module();
        InteropType broken = new(new("broken"), "StopHandle", NativeTypeCategory.Alias, 8, 8) { elementType = new("broken") };
        InteropFunction stop = module.functions[1] with { parameters = [module.functions[1].parameters[0] with { type = broken.id }] };
        Assert.Throws<InvalidOperationException>(() => new SafetyAnalyzer().Analyze(Copy(module, [module.functions[0], stop], module.types.Add(broken))));
    }

    private static InteropModule Module(bool asynchronous = false, EvidenceLevel level = EvidenceLevel.UserAsserted,
        NativeCallbackThreading threading = NativeCallbackThreading.AnyThread)
    {
        Evidence evidence = new(level, "fixture:exact-stop-and-join-protocol");
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType empty = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropType pointer = new(new("pointer"), "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = empty.id };
        InteropType qualified = new(new("qualified-handle"), "void* const", NativeTypeCategory.Qualified, 8, 8) { elementType = pointer.id, qualifier = "Const" };
        InteropType alias = new(new("handle-alias"), "RegistrationToken", NativeTypeCategory.Alias, 8, 8) { elementType = qualified.id };
        InteropType integers = new(new("integer-pointer"), "int*", NativeTypeCategory.Pointer, 8, 8) { elementType = integer.id };
        InteropType callable = new(new("callable"), "Completion", NativeTypeCategory.Function, null, null)
        { returnType = empty.id, parameterTypes = [integer.id, integer.id, pointer.id], callingConvention = "C" };
        InteropType callbackPointer = new(new("callback-pointer"), "Completion*", NativeTypeCategory.Pointer, 8, 8) { elementType = callable.id };
        CallbackContract callback = new(callable.id, new(asynchronous ? NativeLifetimeKind.Operation : NativeLifetimeKind.Registration, null, evidence), evidence)
        {
            retained = true, threading = threading, userDataParameter = new("state"), callableUserDataIndex = 2,
            unregisterFunction = new("stop"), unregisterQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations,
            registration = new(new("handle"), NativeQuiescenceKind.NoActiveNativeInvocations, threading == NativeCallbackThreading.EventLoop ? "eventLoop" : "any", evidence)
            { unregisterIsNonBlocking = threading == NativeCallbackThreading.EventLoop ? true : null }
        };
        InteropFunction start = new(new("start"), "Start", alias.id,
            [new(new("callback"), "callback", callbackPointer.id) { callback = callback }, new(new("state"), "state", pointer.id)], "C")
        { source = new("include/operation.h", 19, 3) };
        InteropFunction stop = new(new("stop"), "StopAndJoin", empty.id, [new(new("handle"), "handle", pointer.id)], "C");
        InteropFunction cancel = new(new("cancel"), "RequestCancellation", empty.id, [new(new("cancel-handle"), "handle", alias.id)], "C");
        if (asynchronous) start = start with
        {
            parameters = start.parameters.Add(new(new("memory"), "memory", integers.id)
            { buffer = new(integer.id, NativeLengthUnit.Elements, NativeParameterDirection.InputOutput, new(NativeLifetimeKind.Operation, null, evidence), evidence)
                { retainsPointer = true, lengthParameter = new("count"), allowsNull = false } }).Add(new(new("count"), "count", integer.id)),
            asynchronous = new(new("callback"), NativeQuiescenceKind.NoNewInvocations, evidence)
            {
                resultArgumentIndex = 0, statusArgumentIndex = 1, successStatus = new(NativeScalarConstantKind.Integer, "0", 4, true, evidence),
                retainedParameters = [new("memory")], cancelFunction = cancel.id, cancellationHandleParameter = new("cancel-handle"),
                cancellationIsNonBlocking = threading == NativeCallbackThreading.EventLoop ? true : null
            }
        };
        return new("registration", "custom-native-target", 8, [integer, empty, pointer, qualified, alias, integers, callable, callbackPointer],
            asynchronous ? [start, stop, cancel] : [start, stop], []);
    }

    private static InteropModule Copy(InteropModule original, ImmutableArray<InteropFunction> functions, ImmutableArray<InteropType>? types = null)
        => new(original.libraryId, original.targetId, original.pointerSize, types ?? original.types, functions, []);
}
