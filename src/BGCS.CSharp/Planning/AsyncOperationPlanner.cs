using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.CSharp.Planning;

/// <summary>Plans an operation-lifetime memory pin and native integral bound channels.</summary>
/// <param name="parameterIndex">The native buffer input.</param>
/// <param name="elementType">The checked unmanaged scalar carrier.</param>
/// <param name="countIndices">Integral native length/capacity channels.</param>
/// <param name="allowsEmpty">Whether an empty memory block may supply a null native address.</param>
public sealed record CSharpAsyncBufferPlan(
    int parameterIndex,
    string elementType,
    ImmutableArray<int> countIndices,
    bool allowsEmpty
)
{
    /// <summary>Gets the exact alignment checked against the actual pinned address before entering native code.</summary>
    public int requiredAlignment { get; init; } = 1;
}

/// <summary>Freezes a completion signal whose native join, rather than callback entry, ends module and pinned-storage use.</summary>
/// <param name="methodIndex">The exact native start import.</param>
/// <param name="unregisterIndex">The exact full native stop-and-join import.</param>
/// <param name="callback">The shared checked static reverse-call ABI.</param>
/// <param name="signature">The public operation factory signature.</param>
public sealed record CSharpAsyncOperationPlan(
    int methodIndex,
    int unregisterIndex,
    CSharpCallbackInputPlan callback,
    CSharpFacadeMethodPlan signature
)
{
    /// <summary>Gets the cancellation-request import, or minus one.</summary>
    public int cancelIndex { get; init; } = -1;
    /// <summary>Gets the completion argument carrying the native result.</summary>
    public int resultIndex { get; init; }
    /// <summary>Gets the completion argument carrying the terminal status.</summary>
    public int statusIndex { get; init; }
    /// <summary>Gets the checked public scalar result type.</summary>
    public string resultType { get; init; } = string.Empty;
    /// <summary>Gets whether the raw result is a native Boolean byte.</summary>
    public bool booleanResult { get; init; }
    /// <summary>Gets the exact native success comparison expression.</summary>
    public string successExpression { get; init; } = string.Empty;
    /// <summary>Gets an exact acknowledged cancellation comparison, or null.</summary>
    public string? cancellationExpression { get; init; }
    /// <summary>Gets buffers whose pins survive the complete native terminal join.</summary>
    public ImmutableArray<CSharpAsyncBufferPlan> buffers { get; init; } = [];
    /// <summary>Gets whether completion and cancellation use the asserted nonblocking event-loop protocol.</summary>
    public bool nonBlockingShutdown { get; init; }
}

internal static class AsyncOperationPlanner
{
    internal static (ImmutableArray<CSharpAsyncOperationPlan> Plans, ImmutableArray<CSharpCallbackOmissionPlan> Omissions) Plan(CSharpEmissionPlan plan)
    {
        var result = ImmutableArray.CreateBuilder<CSharpAsyncOperationPlan>();
        var omissions = ImmutableArray.CreateBuilder<CSharpCallbackOmissionPlan>();
        ReverseCallbackPlanner reverse = new(plan);
        var functionsById = plan.api.interop.functions.ToLookup(function => function.id);
        for (int methodIndex = 0; methodIndex < plan.methods.Length; methodIndex++)
        {
            CSharpMethodPlan method = plan.methods[methodIndex];
            InteropFunction function = functionsById[method.nativeExport].Single();
            if (function.asynchronous is not { } asynchronous) continue;
            try
            {
                if (!ReverseCallbackPlanner.Strong(asynchronous.evidence) || !Enum.IsDefined(asynchronous.completionQuiescence) || !Enum.IsDefined(asynchronous.cancellationQuiescence)
                    || asynchronous.completionQuiescence == NativeQuiescenceKind.Unknown || asynchronous.retainedParameters.IsDefault
                    || asynchronous.retainedParameters.Distinct().Count() != asynchronous.retainedParameters.Length
                    || asynchronous.resultArgumentIndex is not int resultIndex || asynchronous.statusArgumentIndex is not int statusIndex)
                    throw new NotSupportedException("An async projection requires explicit terminal completion channels, evidence and a separate full native join.");
                int completionIndex = Array.FindIndex(function.parameters.ToArray(), parameter => parameter.id == asynchronous.completionCallback);
                if (completionIndex < 0 || function.parameters.Count(parameter => parameter.callback is not null) != 1)
                    throw new NotSupportedException("An async operation requires exactly one explicitly retained native completion callback.");
                CallbackContract contract = function.parameters[completionIndex].callback
                    ?? throw new NotSupportedException("The async completion callback has no native registration protocol.");
                if (contract.lifetime.kind != NativeLifetimeKind.Operation)
                    throw new NotSupportedException("An async completion callback requires an explicit operation lifetime.");
                CallbackRegistrationContract registration = RetainedCallbackPlanner.ValidateRegistration(plan, function, contract, reverse, out int unregisterIndex);
                if (registration.kind != NativeCallbackRegistrationKind.ReturnedHandle)
                    throw new NotSupportedException("The managed async projection requires a returned native operation handle for cancellation and terminal join.");
                CSharpCallbackInputPlan callback = reverse.PlanInput(function, method, completionIndex, [], requireStatic: true);
                if (callback.resultType != "void" || resultIndex < 0 || statusIndex < 0 || resultIndex == statusIndex
                    || resultIndex >= callback.parameterTypes.Length || statusIndex >= callback.parameterTypes.Length
                    || resultIndex == callback.callableUserDataIndex || statusIndex == callback.callableUserDataIndex)
                    throw new NotSupportedException("The async completion must be a void callback with distinct scalar result, integral status and user-data channels.");
                InteropType callable = plan.api.interop.GetType(contract.callableType);
                if (!reverse.Scalar(callable.parameterTypes[resultIndex], allowVoid: false))
                    throw new NotSupportedException("The async result must have an exact fixed scalar representation.");
                string success = Status(asynchronous.successStatus, callable.parameterTypes[statusIndex]);
                string? cancelled = asynchronous.cancellationStatus is { } cancellation ? Status(cancellation, callable.parameterTypes[statusIndex]) : null;
                if (cancelled == success) throw new NotSupportedException("Async success and cancellation statuses must be distinct.");
                int cancelIndex = -1;
                if (asynchronous.cancelFunction is StableId cancel)
                {
                    if (RetainedCallbackPlanner.UsesEventLoopShutdown(contract) && asynchronous.cancellationIsNonBlocking != true)
                        throw new NotSupportedException("Event-loop async cancellation requires an explicit nonblocking native request assertion.");
                    cancelIndex = Array.FindIndex(plan.methods.ToArray(), candidate => candidate.nativeExport == cancel);
                    if (cancelIndex < 0) throw new NotSupportedException("The async cancellation-request export is absent.");
                    InteropFunction request = functionsById[cancel].Single();
                    if (request.parameters.Length != 1 || request.parameters[0].id != asynchronous.cancellationHandleParameter
                        || reverse.Resolve(request.parameters[0].type).id != reverse.Resolve(function.returnType).id || reverse.Resolve(request.returnType).primitiveKind != "Void"
                        || request.parameters[0] is { callback: not null } or { ownership: not null } or { buffer: not null } or { text: not null } or { singleValue: not null }
                        || request.error is not null || request.asynchronous is not null || request.returnOwnership is not null || request.returnString is not null)
                        throw new NotSupportedException("Cancellation must be a separate fixed void request taking the exact returned operation handle.");
                }
                HashSet<int> hidden = [completionIndex, callback.userDataIndex];
                var buffers = ImmutableArray.CreateBuilder<CSharpAsyncBufferPlan>();
                foreach (var input in function.parameters.Select((
                    parameter,
                    index
                ) => (Parameter: parameter, Index: index)))
                {
                    if (hidden.Contains(input.Index) || input.Parameter.buffer is not { } buffer) continue;
                    InteropType pointer = reverse.Resolve(input.Parameter.type);
                    InteropType element = reverse.Resolve(buffer.elementType);
                    string carrier = reverse.Carrier(buffer.elementType);
                    if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId pointed
                        || reverse.Resolve(pointed).id != element.id || element.category != NativeTypeCategory.Primitive
                        || carrier is not ("byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "float" or "double")
                        || !ReverseCallbackPlanner.Strong(buffer.evidence) || !ReverseCallbackPlanner.Strong(buffer.lifetime.evidence)
                        || buffer.lifetime.kind != NativeLifetimeKind.Operation || buffer.retainsPointer != true
                        || buffer.lengthUnit != NativeLengthUnit.Elements || buffer.direction is not (NativeParameterDirection.Input or NativeParameterDirection.Output or NativeParameterDirection.InputOutput)
                        || buffer.allowsNull is null || buffer.writtenCountParameter is not null || input.Parameter.ownership is not null || input.Parameter.text is not null || input.Parameter.singleValue is not null
                        || element.alignment is not int elementAlignment || elementAlignment <= 0
                        || (buffer.direction is NativeParameterDirection.Output or NativeParameterDirection.InputOutput) && IsConst(pointed))
                        throw new NotSupportedException("An async memory projection requires an explicitly retained operation-lifetime scalar buffer with element bounds and known null/alignment behavior.");
                    int[] counts = new[] { buffer.lengthParameter, buffer.capacityParameter }.Where(id => id is not null).Distinct()
                        .Select(id => Array.FindIndex(function.parameters.ToArray(), parameter => parameter.id == id)).ToArray();
                    if (counts.Length == 0 || counts.Any(index => index < 0 || hidden.Contains(index) || function.parameters[index].buffer is not null
                        || function.parameters[index].callback is not null || function.parameters[index].text is not null || function.parameters[index].ownership is not null || function.parameters[index].singleValue is not null
                        || reverse.Carrier(function.parameters[index].type) is not ("byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint")))
                        throw new NotSupportedException("Async memory requires distinct exact integral length/capacity channels.");
                    foreach (int count in counts) hidden.Add(count);
                    int requiredAlignment = Math.Max(elementAlignment, buffer.requiredAlignment ?? 1);
                    if (requiredAlignment <= 0 || (requiredAlignment & (requiredAlignment - 1)) != 0)
                        throw new NotSupportedException("Async memory requires an exact positive power-of-two native alignment.");
                    buffers.Add(new(input.Index, carrier, counts.ToImmutableArray(), buffer.allowsNull == true) { requiredAlignment = requiredAlignment });
                }
                if (!asynchronous.retainedParameters.ToHashSet().SetEquals(buffers.Select(buffer => function.parameters[buffer.parameterIndex].id)))
                    throw new NotSupportedException("Every retained async input must have a checked memory pin; unknown or missing retained storage cannot be inferred.");
                foreach (var input in function.parameters.Select((
                    parameter,
                    index
                ) => (Parameter: parameter, Index: index)))
                    if (!hidden.Contains(input.Index) && !buffers.Any(buffer => buffer.parameterIndex == input.Index)
                        && (!reverse.Scalar(input.Parameter.type, allowVoid: false) || input.Parameter.ownership is not null || input.Parameter.text is not null
                            || input.Parameter.singleValue is not null || input.Parameter.buffer is not null || method.valueProjections.ContainsKey(input.Index)))
                        throw new NotSupportedException("Other async factory inputs require direct native scalars without unproven retained conversions.");
                int[] publicIndices = Enumerable.Range(0, function.parameters.Length).Where(index => !hidden.Contains(index)).ToArray();
                bool boolean = reverse.Resolve(callable.parameterTypes[resultIndex]).primitiveKind == "Bool";
                string resultType = boolean ? "bool" : callback.parameterTypes[resultIndex];
                result.Add(new(methodIndex, unregisterIndex, callback, new(method.name.TrimStart('@') + "Async",
                    "global::" + CSharpNames.Namespace(plan.runtimeNamespace) + ".NativeOperation<" + resultType + ">",
                    publicIndices.Select(index => buffers.SingleOrDefault(buffer => buffer.parameterIndex == index) is { } memory
                        ? "global::System.Memory<" + memory.elementType + ">" : method.parameterTypes[index]).ToImmutableArray(),
                    publicIndices.Select(index => method.parameterNames[index]).ToImmutableArray())
                { nativeExport = function.id, inputIndices = publicIndices.ToImmutableArray(), documentation = method.documentation })
                {
                    cancelIndex = cancelIndex,
                    resultIndex = resultIndex,
                    statusIndex = statusIndex,
                    resultType = resultType,
                    booleanResult = boolean,
                    nonBlockingShutdown = RetainedCallbackPlanner.UsesEventLoopShutdown(contract),
                    successExpression = success,
                    cancellationExpression = cancelled,
                    buffers = buffers.ToImmutable()
                });
            }
            catch (NotSupportedException failure)
            { omissions.Add(new(function.id, asynchronous.completionCallback, failure.Message) { source = function.source }); }
        }
        return (result.ToImmutable(), omissions.ToImmutable());

        bool IsConst(StableId id)
        {
            HashSet<StableId> seen = [];
            InteropType type = plan.api.interop.GetType(id);
            while (seen.Add(type.id))
            {
                if (type.category == NativeTypeCategory.Qualified && type.qualifier == "Const") return true;
                if (type.category is not (NativeTypeCategory.Qualified or NativeTypeCategory.Alias) || type.elementType is not StableId element) return false;
                type = plan.api.interop.GetType(element);
            }
            return false;
        }

        string Status(
            NativeScalarConstant? value,
            StableId id
        ) {
            InteropType type = reverse.Resolve(id);
            while (type.category == NativeTypeCategory.Enumeration && type.elementType is StableId element) type = reverse.Resolve(element);
            if (value is null || !ReverseCallbackPlanner.Strong(value.evidence) || value.kind != NativeScalarConstantKind.Integer || value.width != type.size
                || value.isSigned is not bool signed || signed != type.isSigned || value.width is not (1 or 2 or 4 or 8)
                || !Regex.IsMatch(value.value, "^-?(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)
                || !BigInteger.TryParse(value.value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger integer))
                throw new NotSupportedException("Async statuses require exact integral values, width, signedness and evidence.");
            BigInteger limit = BigInteger.One << (value.width * 8 - (signed ? 1 : 0));
            if (integer < (signed ? -limit : BigInteger.Zero) || integer >= limit) throw new NotSupportedException("An async status exceeds its native representation.");
            return "(" + reverse.Carrier(id) + ")(" + integer.ToString(CultureInfo.InvariantCulture) + (value.width == 8 ? signed ? "L" : "UL" : value.width == 4 && !signed ? "U" : "") + ")";
        }
    }
}
