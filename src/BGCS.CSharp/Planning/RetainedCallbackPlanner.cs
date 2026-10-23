using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.CSharp.Planning;

/// <summary>Freezes one native registration identity with an independently declared complete native stop-and-join boundary.</summary>
/// <param name="methodIndex">The native registration import.</param>
/// <param name="unregisterIndex">The exact native stop-and-join import.</param>
/// <param name="callback">The shared checked static reverse-call ABI and fallback.</param>
/// <param name="registrationName">The public registration type.</param>
/// <param name="nativeCallName">The public explicitly borrowed native-use delegate.</param>
/// <param name="handleType">The exact raw registration handle carrier.</param>
/// <param name="signature">The projected registration factory signature.</param>
public sealed record CSharpRetainedCallbackPlan(
    int methodIndex,
    int unregisterIndex,
    CSharpCallbackInputPlan callback,
    string registrationName,
    string nativeCallName,
    string handleType,
    CSharpFacadeMethodPlan signature
)
{
    /// <summary>Gets the declared native registration identity.</summary>
    public NativeCallbackRegistrationKind kind { get; init; }
    /// <summary>Gets the exact scalar success comparison for a callback/state pair, or null for a void factory.</summary>
    public string? successExpression { get; init; }
    /// <summary>Gets the native unregister callback argument index, or minus one for a returned handle.</summary>
    public int unregisterCallbackIndex { get; init; } = -1;
    /// <summary>Gets the native unregister user-data argument index, or minus one for a returned handle.</summary>
    public int unregisterUserDataIndex { get; init; } = -1;
    /// <summary>Gets whether cleanup uses the asserted nonblocking event-loop protocol.</summary>
    public bool nonBlockingShutdown { get; init; }
}

internal static class RetainedCallbackPlanner
{
    internal static bool HasIntent(CallbackContract contract) => contract.registration is not null
        || contract.retained == true || contract.lifetime.kind is NativeLifetimeKind.Registration or NativeLifetimeKind.Operation;
    internal static bool UsesEventLoopShutdown(CallbackContract contract) => contract.threading == NativeCallbackThreading.EventLoop
        && contract.registration?.unregisterIsNonBlocking == true;

    internal static (ImmutableArray<CSharpRetainedCallbackPlan> Plans, ImmutableArray<CSharpCallbackOmissionPlan> Omissions) Plan(CSharpEmissionPlan plan)
    {
        var result = ImmutableArray.CreateBuilder<CSharpRetainedCallbackPlan>();
        var omissions = ImmutableArray.CreateBuilder<CSharpCallbackOmissionPlan>();
        ReverseCallbackPlanner reverse = new(plan);
        var functionsById = plan.api.interop.functions.ToLookup(function => function.id);
        for (int methodIndex = 0; methodIndex < plan.methods.Length; methodIndex++)
        {
            CSharpMethodPlan method = plan.methods[methodIndex];
            InteropFunction function = functionsById[method.nativeExport].Single();
            var callbacks = function.parameters.Select((
                parameter,
                index
            ) => (Parameter: parameter, Index: index))
                .Where(item => item.Parameter.callback is { } contract && HasIntent(contract)).ToArray();
            if (callbacks.Length == 0 || function.asynchronous is not null) continue;
            try
            {
                if (callbacks.Length != 1 || function.parameters.Count(parameter => parameter.callback is not null) != 1)
                    throw new NotSupportedException("A managed registration currently requires exactly one retained native callback.");
                var item = callbacks[0];
                CallbackContract contract = item.Parameter.callback!;
                CallbackRegistrationContract registration = ValidateRegistration(plan, function, contract, reverse, out int unregisterIndex);
                if (contract.lifetime.kind != NativeLifetimeKind.Registration)
                    throw new NotSupportedException("A retained callback registration requires an explicit registration lifetime; operation lifetimes use the async projection.");
                CSharpCallbackInputPlan callback = reverse.PlanInput(function, method, item.Index, [], requireStatic: true);
                foreach (var input in function.parameters.Select((
                    parameter,
                    index
                ) => (Parameter: parameter, Index: index)))
                {
                    if (input.Index == callback.parameterIndex || input.Index == callback.userDataIndex) continue;
                    if (!reverse.Scalar(input.Parameter.type, allowVoid: false) || input.Parameter.ownership is not null || input.Parameter.text is not null
                        || input.Parameter.buffer is not null || input.Parameter.singleValue is not null || method.valueProjections.ContainsKey(input.Index))
                        throw new NotSupportedException("A callback registration currently requires other factory arguments to be direct native scalars with no retained storage conversion.");
                }
                string suffix = callback.stateName["__BgcsCallbackState_".Length..];
                string name = method.name.TrimStart('@') + "Registration_" + suffix;
                int[] publicIndices = Enumerable.Range(0, function.parameters.Length).Where(index => index != callback.userDataIndex).ToArray();
                InteropFunction unregister = functionsById[contract.unregisterFunction!.Value].Single();
                result.Add(new(methodIndex, unregisterIndex, callback, name, method.name.TrimStart('@') + "NativeUse_" + suffix,
                    registration.kind == NativeCallbackRegistrationKind.ReturnedHandle ? method.returnType : "nint", new(method.name, SharedContextPlanner.ContextType(plan) + "." + name,
                        publicIndices.Select(index => index == callback.parameterIndex ? SharedContextPlanner.ContextType(plan) + "." + callback.delegateName
                            : method.parameterTypes[index]).ToImmutableArray(), publicIndices.Select(index => method.parameterNames[index]).ToImmutableArray())
                    { nativeExport = function.id, inputIndices = publicIndices.ToImmutableArray(), documentation = method.documentation })
                {
                    kind = registration.kind,
                    nonBlockingShutdown = UsesEventLoopShutdown(contract),
                    successExpression = registration.successfulReturn is { } success ? reverse.ScalarExpression(success, function.returnType, method.returnType) : null,
                    unregisterCallbackIndex = Array.FindIndex(unregister.parameters.ToArray(), parameter => parameter.id == registration.unregisterCallbackParameter),
                    unregisterUserDataIndex = Array.FindIndex(unregister.parameters.ToArray(), parameter => parameter.id == registration.unregisterUserDataParameter)
                });
            }
            catch (NotSupportedException failure)
            {
                foreach (var item in callbacks) omissions.Add(new(function.id, item.Parameter.id, failure.Message) { source = item.Parameter.source ?? function.source });
            }
        }
        return (result.ToImmutable(), omissions.ToImmutable());
    }

    internal static CallbackRegistrationContract ValidateRegistration(
        CSharpEmissionPlan plan,
        InteropFunction function,
        CallbackContract contract,
        ReverseCallbackPlanner reverse,
        out int unregisterIndex
    ) {
        CallbackRegistrationContract registration = contract.registration
            ?? throw new NotSupportedException("A retained callback needs an explicit registration identity and exact native unregister parameters.");
        if (!ReverseCallbackPlanner.Strong(contract.evidence) || !ReverseCallbackPlanner.Strong(contract.lifetime.evidence)
            || !ReverseCallbackPlanner.Strong(registration.evidence) || contract.retained != true
            || registration.releaseThreadDomain != "any" && !(UsesEventLoopShutdown(contract) && registration.releaseThreadDomain == "eventLoop")
            || contract.unregisterQuiescence != NativeQuiescenceKind.NoActiveNativeInvocations)
            throw new NotSupportedException("A managed registration requires an explicit native cleanup thread domain and a full native-frame stop-and-join guarantee.");
        if (plan.managedRuntime is { allowsBlockingDisposal: false }
            && !UsesEventLoopShutdown(contract))
            throw new NotSupportedException("This runtime requires an explicitly nonblocking native unregister protocol and EventLoop callback delivery on the captured dispatcher.");
        if (!Enum.IsDefined(registration.kind) || function.error is not null || function.returnOwnership is not null || function.returnString is not null)
            throw new NotSupportedException("A callback registration requires one declared native identity without competing result cleanup protocols.");
        unregisterIndex = Array.FindIndex(plan.methods.ToArray(), method => method.nativeExport == contract.unregisterFunction);
        if (unregisterIndex < 0) throw new NotSupportedException("The exact native callback unregister export is absent from the selected module.");
        InteropFunction unregister = plan.api.interop.functions.Single(function => function.id == contract.unregisterFunction);
        if (reverse.Resolve(unregister.returnType).primitiveKind != "Void" || unregister.isVariadic || !unregister.hasPrototype
            || unregister.error is not null || unregister.asynchronous is not null || unregister.returnOwnership is not null || unregister.returnString is not null
            || unregister.parameters.Any(parameter => parameter.callback is not null || parameter.buffer is not null || parameter.text is not null || parameter.singleValue is not null || parameter.ownership is not null))
            throw new NotSupportedException("Native stop-and-join must be a fixed void operation without another parameter cleanup protocol.");
        if (registration.kind == NativeCallbackRegistrationKind.ReturnedHandle)
        {
            if (registration.nullResultQuiescence != NativeQuiescenceKind.NoActiveNativeInvocations || reverse.Resolve(function.returnType).category != NativeTypeCategory.Pointer
                || unregister.parameters.Length != 1 || unregister.parameters[0].id != registration.unregisterHandleParameter
                || reverse.Resolve(unregister.parameters[0].type).id != reverse.Resolve(function.returnType).id
                || registration.unregisterCallbackParameter is not null || registration.unregisterUserDataParameter is not null || registration.successfulReturn is not null
                || registration.failureResultQuiescence != NativeQuiescenceKind.Unknown)
                throw new NotSupportedException("Native returned-handle stop-and-join must take exactly the original returned pointer and declare no active native use on null failure.");
        }
        else
        {
            InteropParameter? callback = unregister.parameters.SingleOrDefault(parameter => parameter.id == registration.unregisterCallbackParameter);
            InteropParameter? state = unregister.parameters.SingleOrDefault(parameter => parameter.id == registration.unregisterUserDataParameter);
            if (unregister.parameters.Length != 2 || callback is null || state is null || callback.id == state.id || !reverse.VoidPointer(state.type)
                || registration.unregisterHandleParameter is not null || registration.nullResultQuiescence != NativeQuiescenceKind.Unknown)
                throw new NotSupportedException("A callback/state unregister must consume exactly the identical callback pointer and opaque user-data channel.");
            InteropType callable = reverse.Resolve(callback.type);
            if (callable.category == NativeTypeCategory.Pointer && callable.elementType is StableId element) callable = reverse.Resolve(element);
            if (callable.category != NativeTypeCategory.Function || callable.id != contract.callableType)
                throw new NotSupportedException("The callback/state unregister prototype differs from the exact registered callback type.");
            if (reverse.Resolve(function.returnType).primitiveKind == "Void")
            {
                if (registration.successfulReturn is not null) throw new NotSupportedException("A void callback/state registration cannot declare a scalar success result.");
            }
            else if (!reverse.Scalar(function.returnType, allowVoid: false) || registration.successfulReturn is null
                || registration.failureResultQuiescence != NativeQuiescenceKind.NoActiveNativeInvocations)
                throw new NotSupportedException("A scalar callback/state registration requires an exact successful return and no active native use after every failed result.");
        }
        return registration;
    }
}
