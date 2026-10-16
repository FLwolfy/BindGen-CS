using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Safety;

// A registration token has a declared terminal protocol. This does not turn it into a
// heap-owned pointer, certify a reverse-call ABI or select a managed runtime projection.
internal static class ReturnedRegistrationSafety
{
    internal static bool HasIntent(InteropFunction function) => function.asynchronous is not null
        || function.parameters.Any(parameter => parameter.callback is { } callback
            && (callback.registration is not null || callback.retained == true
                || callback.lifetime?.kind is NativeLifetimeKind.Registration or NativeLifetimeKind.Operation));

    internal static bool IsComplete(
        InteropModule module,
        InteropFunction function
    ) {
        if (!HasIntent(function) || function.returnOwnership is not null || function.returnString is not null
            || function.error is not null || !function.hasPrototype || function.isVariadic) return false;
        InteropParameter[] callbacks = function.parameters.Where(parameter => parameter.callback is not null).ToArray();
        if (callbacks.Length != 1) return false;
        InteropParameter parameter = callbacks[0];
        CallbackContract callback = parameter.callback!;
        if (callback.registration is not { kind: NativeCallbackRegistrationKind.ReturnedHandle } registration
            || !Explicit(callback.evidence) || callback.lifetime is not { } lifetime || !Explicit(lifetime.evidence)
            || !Explicit(registration.evidence) || callback.retained != true || !Plain(parameter)
            || callback.unregisterQuiescence != NativeQuiescenceKind.NoActiveNativeInvocations
            || registration.nullResultQuiescence != NativeQuiescenceKind.NoActiveNativeInvocations
            || registration.failureResultQuiescence != NativeQuiescenceKind.Unknown
            || registration.unregisterCallbackParameter is not null || registration.unregisterUserDataParameter is not null
            || registration.successfulReturn is not null
            || callback.threading is not (NativeCallbackThreading.CallerThread or NativeCallbackThreading.AnyThread or NativeCallbackThreading.EventLoop)) return false;
        bool eventLoop = callback.threading == NativeCallbackThreading.EventLoop;
        if (eventLoop && registration.unregisterIsNonBlocking != true
            || registration.releaseThreadDomain != "any" && !(eventLoop && registration.releaseThreadDomain == "eventLoop")) return false;

        InteropType callable = Resolve(parameter.type);
        if (callable.category == NativeTypeCategory.Pointer && callable.elementType is StableId element) callable = Resolve(element);
        if (callable.category != NativeTypeCategory.Function || callable.id != callback.callableType
            || !callable.hasPrototype || callable.isVariadic || callable.returnType is not StableId result
            || callable.parameterTypes.IsDefault) return false;
        _ = Resolve(result);
        foreach (StableId argument in callable.parameterTypes) _ = Resolve(argument);
        InteropParameter? state = function.parameters.SingleOrDefault(candidate => candidate.id == callback.userDataParameter);
        if (state is null || state.id == parameter.id || !Plain(state) || state.callback is not null || !VoidPointer(state.type)
            || callback.callableUserDataIndex is not int stateIndex || stateIndex < 0 || stateIndex >= callable.parameterTypes.Length
            || !VoidPointer(callable.parameterTypes[stateIndex])) return false;
        if (callback.unregisterFunction is not StableId stopId || stopId == function.id
            || !Stop(module.functions.SingleOrDefault(candidate => candidate.id == stopId), registration.unregisterHandleParameter)) return false;

        if (function.asynchronous is not { } asynchronous) return lifetime.kind == NativeLifetimeKind.Registration;
        if (lifetime.kind != NativeLifetimeKind.Operation || !Explicit(asynchronous.evidence)
            || asynchronous.completionCallback != parameter.id
            || asynchronous.completionQuiescence is not (NativeQuiescenceKind.NoNewInvocations or NativeQuiescenceKind.NoActiveNativeInvocations)
            || !Enum.IsDefined(asynchronous.cancellationQuiescence) || asynchronous.retainedParameters.IsDefault
            || asynchronous.retainedParameters.Distinct().Count() != asynchronous.retainedParameters.Length) return false;
        foreach (StableId retained in asynchronous.retainedParameters)
        {
            InteropParameter? input = function.parameters.SingleOrDefault(candidate => candidate.id == retained);
            if (input is null || input.buffer is not { retainsPointer: true } buffer || input.id == state.id || input.id == parameter.id
                || !Explicit(buffer.evidence) || buffer.lifetime is not { kind: NativeLifetimeKind.Operation } bufferLifetime
                || !Explicit(bufferLifetime.evidence)) return false;
        }
        if (asynchronous.cancelFunction is StableId cancelId)
        {
            if (cancelId == function.id || cancelId == stopId || eventLoop && asynchronous.cancellationIsNonBlocking != true
                || !Stop(module.functions.SingleOrDefault(candidate => candidate.id == cancelId), asynchronous.cancellationHandleParameter)) return false;
        }
        else if (asynchronous.cancellationHandleParameter is not null || asynchronous.cancellationIsNonBlocking is not null
            || asynchronous.cancellationQuiescence != NativeQuiescenceKind.Unknown) return false;
        return true;

        bool Stop(
            InteropFunction? stop,
            StableId? handle
        ) => stop is not null && stop.hasPrototype && !stop.isVariadic
            && stop.parameters.Length == 1 && stop.parameters[0].id == handle && Plain(stop.parameters[0])
            && stop.parameters[0].callback is null && Resolve(stop.parameters[0].type).id == Resolve(function.returnType).id
            && Void(stop.returnType) && stop.error is null && stop.asynchronous is null
            && stop.returnOwnership is null && stop.returnString is null;

        bool VoidPointer(StableId id) => Resolve(id) is { category: NativeTypeCategory.Pointer, elementType: StableId pointed } && Void(pointed);
        bool Void(StableId id) => Resolve(id) is { category: NativeTypeCategory.Primitive, primitiveKind: "Void" };

        InteropType Resolve(StableId id)
        {
            HashSet<StableId> seen = [];
            while (true)
            {
                if (string.IsNullOrWhiteSpace(id.value) || !seen.Add(id)) throw Invalid(id);
                InteropType type;
                try { type = module.GetType(id); }
                catch (KeyNotFoundException failure) { throw Invalid(id, failure); }
                if (!Enum.IsDefined(type.category)) throw Invalid(id);
                if (type.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified)) return type;
                if (type.elementType is not StableId next) throw Invalid(id);
                id = next;
            }
        }

        InvalidOperationException Invalid(
            StableId id,
            Exception? inner = null
        ) => new(
            $"Registration type graph for native export '{function.exportName}' is cyclic or incomplete at '{id.value}'. Safety analysis requires intact callback and handle identities.", inner);
    }

    private static bool Plain(InteropParameter parameter) => parameter.ownership is null && parameter.text is null
        && parameter.buffer is null && parameter.singleValue is null;

    private static bool Explicit(Evidence? evidence) => evidence is { level: EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified }
        && !string.IsNullOrWhiteSpace(evidence.origin);
}
