using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Safety;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;

namespace BGCS.CSharp.Planning;

/// <summary>Freezes a synchronous native call with rooted managed callbacks and explicit native-return quiescence.</summary>
/// <param name="methodIndex">The exact native import index.</param>
/// <param name="callbacks">Rooted reverse-call protocols in native parameter order.</param>
/// <param name="publicIndices">Native arguments exposed by the managed overload.</param>
/// <param name="signature">The exact forwarding signature shared by aliases, views and a static facade.</param>
public sealed record CSharpCallbackMethodPlan(
    int methodIndex,
    ImmutableArray<CSharpCallbackInputPlan> callbacks,
    ImmutableArray<int> publicIndices,
    CSharpFacadeMethodPlan signature
)
{
    /// <summary>Gets the existing checked status/owned-result operation reused by this reverse-call wrapper.</summary>
    public CSharpStatusMethodPlan? statusOperation { get; init; }
    /// <summary>Gets the exact independently owned receiver and its object-local forwarding signature.</summary>
    public CSharpCallbackReceiverPlan? receiver { get; init; }
}

/// <summary>Projects a call-only callback through an existing owned receiver without exposing its address.</summary>
/// <param name="objectName">The exact nested owned wrapper name.</param>
/// <param name="nativeType">The original native class identity.</param>
/// <param name="instanceIndex">The exact native self parameter.</param>
/// <param name="objectSignature">The managed member signature with self omitted.</param>
public sealed record CSharpCallbackReceiverPlan(
    string objectName,
    StableId? nativeType,
    int instanceIndex,
    CSharpFacadeMethodPlan objectSignature
)
{
    /// <summary>Gets the exact generated bridge owner when no original native owner exists.</summary>
    public StableId? generatedType { get; init; }
}

/// <summary>Freezes one exact reverse-call ABI, explicit fallback and optional static user-data token protocol.</summary>
/// <param name="parameterIndex">The native argument receiving the callback address.</param>
/// <param name="userDataIndex">The outer user-data argument, or minus one.</param>
/// <param name="callableUserDataIndex">The callback-side state argument, or minus one.</param>
/// <param name="delegateName">The allocated public managed callback type.</param>
/// <param name="stateName">The allocated private state type.</param>
/// <param name="thunkName">The allocated static reverse-call method.</param>
/// <param name="resultType">The exact callback result carrier.</param>
/// <param name="parameterTypes">The exact callback input carriers.</param>
/// <param name="publicIndices">Callback arguments exposed to managed code.</param>
/// <param name="callingConvention">The implemented native reverse-call convention.</param>
/// <param name="fallbackExpression">The checked native failure result expression.</param>
/// <param name="staticThunk">Whether user data selects an AOT-compatible generated thunk.</param>
public sealed record CSharpCallbackInputPlan(
    int parameterIndex,
    int userDataIndex,
    int callableUserDataIndex,
    string delegateName,
    string stateName,
    string thunkName,
    string resultType,
    ImmutableArray<string> parameterTypes,
    ImmutableArray<int> publicIndices,
    string callingConvention,
    string fallbackExpression,
    bool staticThunk
)
{
    /// <summary>Gets an existing exact named ABI delegate reused by a CoreCLR call-only wrapper, or null for a generated managed handler.</summary>
    public string? abiDelegateType { get; init; }
}

/// <summary>Explains a missing optional managed callback wrapper without discarding its correct raw ABI.</summary>
/// <param name="nativeExport">The exact native import identity.</param>
/// <param name="callbackParameter">The original callback parameter identity.</param>
/// <param name="reason">The missing lifetime or representation requirement.</param>
public sealed record CSharpCallbackOmissionPlan(
    StableId nativeExport,
    StableId callbackParameter,
    string reason
)
{
    /// <summary>Gets the original native parameter or callable location when available.</summary>
    public SourceLocation? source { get; init; }
}

internal static class CallScopedCallbackPlanner
{
    internal static (ImmutableArray<CSharpCallbackMethodPlan> Plans, ImmutableArray<CSharpCallbackOmissionPlan> Omissions) Plan(CSharpEmissionPlan plan)
    {
        var result = ImmutableArray.CreateBuilder<CSharpCallbackMethodPlan>();
        var omissions = ImmutableArray.CreateBuilder<CSharpCallbackOmissionPlan>();
        InteropModule module = plan.api.interop;
        var functionsById = module.functions.ToLookup(function => function.id);
        var membersByExport = plan.api.methods.ToLookup(member => member.nativeExport);
        ReverseCallbackPlanner reverse = new(plan);
        for (int methodIndex = 0; methodIndex < plan.methods.Length; methodIndex++)
        {
            CSharpMethodPlan method = plan.methods[methodIndex];
            InteropFunction function = functionsById[method.nativeExport].Single();
            var declared = function.parameters.Select((
                parameter,
                index
            ) => (Parameter: parameter, Index: index)).Where(item => item.Parameter.callback is not null).ToArray();
            if (declared.Length == 0) continue;
            if (declared.Any(item => RetainedCallbackPlanner.HasIntent(item.Parameter.callback!))) continue;
            try
            {
                if (membersByExport[function.id].Single().receiver?.kind == ManagedReceiverKind.ModuleBorrowed)
                    throw new NotSupportedException("A module-borrowed callback receiver needs its own independently modeled call protocol and remains raw.");
                CSharpStatusMethodPlan? status = function.error is not null
                    ? plan.statusMethods.SingleOrDefault(operation => operation.methodIndex == methodIndex) : null;
                if (function.error is not null && status is null
                    || function.asynchronous is not null || function.returnOwnership is not null || function.returnString is not null
                    || !(reverse.Scalar(function.returnType, allowVoid: true) || reverse.Resolve(function.returnType).category == NativeTypeCategory.Pointer || method.returnProjection is { fromNativeExpression: not null }))
                    throw new NotSupportedException("A call-scoped callback wrapper requires a direct scalar result or an independently checked status/owned-result cleanup operation.");
                CSharpObjectPlan? receiver = status is { instanceIndex: >= 0 } ? Receiver(status, function) : null;
                var callbacks = ImmutableArray.CreateBuilder<CSharpCallbackInputPlan>();
                HashSet<int> userDataIndices = [];
                foreach (var item in declared)
                {
                    CallbackContract contract = item.Parameter.callback!;
                    if (contract.lifetime.kind != NativeLifetimeKind.Call || contract.retained != false)
                        throw new NotSupportedException("A managed call-scoped callback requires an explicit call lifetime and no retention.");
                    if (contract.unregisterFunction is not null || contract.unregisterQuiescence != NativeQuiescenceKind.Unknown)
                        throw new NotSupportedException("A retained unregister protocol must use its own native registration and join projection.");
                    callbacks.Add(reverse.PlanInput(function, method, item.Index, userDataIndices));
                }
                HashSet<int> callbackIndices = callbacks.Select(callback => callback.parameterIndex).ToHashSet();
                foreach (var item in function.parameters.Select((
                    parameter,
                    index
                ) => (Parameter: parameter, Index: index)))
                {
                    if (callbackIndices.Contains(item.Index) || userDataIndices.Contains(item.Index)) continue;
                    if (receiver is not null && item.Index == status!.instanceIndex) continue;
                    if (status is not null && !status.inputIndices.Contains(item.Index)) continue;
                    if (method.stringInputs.Any(input => input.parameterIndex == item.Index) || method.bufferInputs.Any(input => input.parameterIndex == item.Index)
                        || method.valueParameters.Any(input => input.parameterIndex == item.Index) || method.bufferProtocols.Any(input => input.writtenCountIndex == item.Index)
                        || method.valueProjections.TryGetValue(item.Index, out CSharpRecipeProjectionPlan? projection) && projection.toNativeExpression is not null)
                        continue;
                    if (!(reverse.Scalar(item.Parameter.type, allowVoid: false) || reverse.Resolve(item.Parameter.type).category == NativeTypeCategory.Pointer)
                        || item.Parameter.ownership is not null || item.Parameter.text is not null
                        || item.Parameter.buffer is not null || item.Parameter.singleValue is not null)
                        throw new NotSupportedException("A call-scoped callback wrapper requires other native inputs to preserve direct scalar or explicit unsafe pointer carriers without competing storage protocols.");
                }
                ImmutableArray<int> inputs = OverloadPlanner.PublicInputs(method, (status is not null ? status.inputIndices : Enumerable.Range(0, function.parameters.Length))
                    .Where(index => !userDataIndices.Contains(index))).ToImmutableArray();
                string DelegateType(int index) => callbacks.SingleOrDefault(callback => callback.parameterIndex == index) is { } callback
                    ? callback.abiDelegateType ?? SharedContextPlanner.ContextType(plan) + "." + callback.delegateName
                    : receiver is not null && index == status!.instanceIndex ? SharedContextPlanner.ContextType(plan) + "." + receiver.name
                    : OverloadPlanner.InputType(method, index);
                string publicResult = status is not null
                    ? status.ownedResult is { } owned ? owned.objectName is string objectName ? SharedContextPlanner.ContextType(plan) + "." + objectName
                        : "global::" + CSharpNames.Namespace(plan.runtimeNamespace) + ".OwnedNativeHandle"
                        : method.valueProjections.TryGetValue(status.resultIndex, out CSharpRecipeProjectionPlan? statusProjection) && statusProjection.fromNativeExpression is not null
                            ? statusProjection.managedType : status.resultType
                    : method.returnProjection is { fromNativeExpression: not null } resultProjection ? resultProjection.managedType : method.returnType;
                CSharpFacadeMethodPlan signature = new(method.name, publicResult,
                    inputs.Select(DelegateType).ToImmutableArray(), inputs.Select(index => method.parameterNames[index]).ToImmutableArray())
                {
                    nativeExport = function.id,
                    inputIndices = inputs,
                    documentation = method.documentation,
                    parameterModifiers = inputs.Select(index => OverloadPlanner.InputModifier(method, index)).ToImmutableArray()
                };
                int[] objectInputs = receiver is null ? [] : Enumerable.Range(0, inputs.Length).Where(index => inputs[index] != status!.instanceIndex).ToArray();
                CSharpFacadeMethodPlan objectSignature = signature with
                {
                    parameterTypes = objectInputs.Select(index => signature.parameterTypes[index]).ToImmutableArray(),
                    parameterNames = objectInputs.Select(index => signature.parameterNames[index]).ToImmutableArray(),
                    parameterModifiers = objectInputs.Select(index => signature.parameterModifiers[index]).ToImmutableArray(),
                    inputIndices = objectInputs.Select(index => inputs[index]).ToImmutableArray()
                };
                result.Add(new(methodIndex, callbacks.ToImmutable(), inputs, signature)
                {
                    statusOperation = status,
                    receiver = receiver is null ? null : new(receiver.name, receiver.nativeType, status!.instanceIndex, objectSignature) { generatedType = receiver.generatedType }
                });
            }
            catch (NotSupportedException failure)
            {
                foreach (var item in declared) omissions.Add(new(function.id, item.Parameter.id, failure.Message) { source = item.Parameter.source ?? function.source });
            }
        }
        return (result.ToImmutable(), omissions.ToImmutable());

        CSharpObjectPlan Receiver(
            CSharpStatusMethodPlan status,
            InteropFunction function
        ) {
            CSharpObjectPlan? owner = plan.objects.SingleOrDefault(candidate => (candidate.nativeType is not null && candidate.nativeType == function.sourceOwnerType && candidate.generatedType is null && function.sourceGeneratedOwnerType is null
                || candidate.generatedType is not null && candidate.generatedType == function.sourceGeneratedOwnerType && candidate.nativeType is null && function.sourceOwnerType is null)
                && candidate.kind == ManagedReceiverKind.Owned
                && candidate.methodIndices.Any(index => plan.statusMethods[index].methodIndex == status.methodIndex));
            if (owner is null)
                throw new NotSupportedException("An instance callback requires an existing exact independently owned native receiver; a module-borrowed or unmodeled receiver remains raw.");
            InteropParameter self = function.parameters[status.instanceIndex];
            if (self.ownership is not { kind: NativeOwnershipKind.Borrowed, lifetime.kind: NativeLifetimeKind.Call } borrowed
                || !ReverseCallbackPlanner.Strong(borrowed.evidence) || !ReverseCallbackPlanner.Strong(borrowed.lifetime.evidence)
                || borrowed.retainsPointer == true || self.buffer is not null || self.text is not null || self.singleValue is not null || self.callback is not null
                || plan.methods[status.methodIndex].valueProjections.ContainsKey(status.instanceIndex))
                throw new NotSupportedException("An owned callback receiver must preserve its exact borrowed Call protocol without contradictory retention or a competing storage conversion.");
            foreach (CSharpStatusMethodPlan factory in plan.statusMethods.Where(operation => operation.ownedResult?.objectName == owner.name))
            {
                InteropFunction creator = functionsById[plan.methods[factory.methodIndex].nativeExport].Single();
                InteropType output = reverse.Resolve(creator.parameters[factory.resultIndex].type);
                if (output.elementType is not StableId pointer || !NativeReceiverTypeMatcher.CanBorrowWithoutRemovingQualifiers(module, pointer, self.type))
                    throw new NotSupportedException("An owned callback receiver cannot remove native const/volatile or unknown pointee qualifiers from a selected factory.");
            }
            return owner;
        }

    }
}
