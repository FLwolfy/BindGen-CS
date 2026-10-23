using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.CSharp.Planning;

internal static class StatusMethodPlanner
{
    internal static ImmutableArray<CSharpStatusMethodPlan> Plan(CSharpEmissionPlan plan)
    {
        Dictionary<StableId, InteropFunction> nativeExports = plan.api.interop.functions.ToDictionary(function => function.id);
        Dictionary<StableId, int> importIndices = plan.methods.Select((
            method,
            index
        ) => (method.nativeExport, index)).ToDictionary(item => item.nativeExport, item => item.index);
        ImmutableArray<CSharpStatusMethodPlan>.Builder methods = ImmutableArray.CreateBuilder<CSharpStatusMethodPlan>();
        for (int index = 0; index < plan.methods.Length; index++)
        {
            CSharpMethodPlan method = plan.methods[index];
            if (!nativeExports.TryGetValue(method.nativeExport, out InteropFunction? function))
                throw new InvalidOperationException("A status method references a missing native export.");
            ErrorContract? error = function.error;
            if (error is null || function.asynchronous is not null || function.returnString is not null || !Known(error.evidence) || error.errorParameter is not StableId errorParameter
                || error.errorOwnership is not { kind: NativeOwnershipKind.Owned, releaseFunction: not null } ownership || !Known(ownership.evidence)
                || !int.TryParse(error.successValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int success)) continue;
            InteropType status = Resolve(function.returnType);
            if (status.category != NativeTypeCategory.Primitive || status.size != 4 || status.isSigned != true) continue;
            int errorIndex = IndexOf(function, errorParameter);
            if (errorIndex < 0 || function.parameters[errorIndex].ownership != ownership) continue;
            InteropType channel = Resolve(function.parameters[errorIndex].type);
            if (channel.category != NativeTypeCategory.Pointer || channel.elementType is not StableId errorPointerId) continue;
            InteropType errorPointer = Resolve(errorPointerId);
            if (errorPointer.category != NativeTypeCategory.Pointer || errorPointer.elementType is not StableId errorTypeId || Resolve(errorTypeId).category != NativeTypeCategory.Record) continue;
            int releaseIndex = Find(ownership.releaseFunction.Value);
            if (releaseIndex < 0) continue;
            InteropFunction release = nativeExports[ownership.releaseFunction.Value];
            if (Resolve(release.returnType).primitiveKind != "Void" || release.parameters.Length != 1 || Resolve(release.parameters[0].type).id != errorPointer.id) continue;
            int resultIndex = error.valueParameter is StableId result ? IndexOf(function, result) : -1;
            if (error.valueParameter is not null && (resultIndex < 0 || resultIndex == errorIndex)) continue;
            string resultSyntax = "void";
            CSharpOwnedResultPlan? ownedResult = null;
            if (resultIndex >= 0)
            {
                InteropParameter parameter = function.parameters[resultIndex];
                InteropType storage = Resolve(parameter.type);
                if (parameter.buffer is not null || parameter.text is not null || storage.category != NativeTypeCategory.Pointer
                    || storage.elementType is not StableId value) continue;
                resultSyntax = method.parameterTypes[resultIndex][..^1].TrimEnd();
                if (parameter.ownership is null)
                {
                    if (!Scalar(value) || Resolve(value).primitiveKind == "Void") continue;
                }
                else
                {
                    ownedResult = OwnedResult(parameter, value);
                    if (ownedResult is null) continue;
                }
            }
            ImmutableArray<int> inputs = Enumerable.Range(0, function.parameters.Length).Where(parameter => parameter != errorIndex && parameter != resultIndex).ToImmutableArray();
            int instance = -1;
            bool unsupported = false;
            foreach (int input in inputs)
            {
                InteropParameter parameter = function.parameters[input];
                if (parameter.callback is not null && parameter.text is null && parameter.buffer is null && parameter.singleValue is null && parameter.ownership is null)
                    continue; // This overload keeps the exact unsafe callback pointer; its managed callback wrapper establishes lifetime separately.
                if (function.parameters.Any(callback => callback.callback?.userDataParameter == parameter.id)
                    && parameter.text is null && parameter.buffer is null && parameter.singleValue is null && parameter.ownership is null
                    && Resolve(parameter.type) is { category: NativeTypeCategory.Pointer, elementType: StableId callbackState }
                    && Resolve(callbackState).primitiveKind == "Void")
                    continue; // The underlying overload preserves the explicit opaque pointer; a checked callback wrapper supplies its token.
                if (method.bufferProtocols.Any(buffer => buffer.writtenCountIndex == input) || method.valueParameters.Any(value => value.parameterIndex == input)) continue;
                if (parameter.text is not null && method.stringInputs.Any(text => text.parameterIndex == input)
                    && parameter.buffer is null && parameter.callback is null && parameter.ownership is null) continue;
                if (parameter.buffer is not null && method.bufferInputs.Any(buffer => buffer.parameterIndex == input)
                    && parameter.text is null && parameter.callback is null) continue;
                if (parameter.text is not null || parameter.buffer is not null || parameter.callback is not null || parameter.singleValue is not null) { unsupported = true; break; }
                if (Scalar(parameter.type) && parameter.ownership is null) continue;
                InteropType pointer = Resolve(parameter.type);
                if (instance >= 0 || pointer.category != NativeTypeCategory.Pointer
                    || pointer.elementType is not StableId receiver || !MatchesOwner(function, Resolve(receiver))
                    || parameter.ownership is not { kind: NativeOwnershipKind.Borrowed, lifetime.kind: NativeLifetimeKind.Call } borrowed
                    || !Known(borrowed.evidence) || !Known(borrowed.lifetime.evidence)) { unsupported = true; break; }
                instance = input;
            }
            if (unsupported) continue;
            methods.Add(new(index, inputs, resultIndex, errorIndex, releaseIndex, success, resultSyntax) { ownedResult = ownedResult, instanceIndex = instance });
        }
        return methods.ToImmutable();

        InteropType Resolve(StableId id)
        {
            InteropType type = plan.api.interop.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = plan.api.interop.GetType(type.elementType!.Value);
            return type;
        }
        static bool MatchesOwner(
            InteropFunction function,
            InteropType record
        )
            => function.sourceOwnerType is not null && function.sourceGeneratedOwnerType is null
                ? record.sourceNativeType == function.sourceOwnerType && record.sourceGeneratedType is null
                : function.sourceGeneratedOwnerType is not null && function.sourceOwnerType is null
                    && record.sourceGeneratedType == function.sourceGeneratedOwnerType && record.sourceNativeType is null;
        bool Scalar(StableId id) => Resolve(id).category is NativeTypeCategory.Primitive or NativeTypeCategory.Enumeration
            || plan.externalTypeProjections.Any(projection => projection.nativeType == Resolve(id).id);
        CSharpOwnedResultPlan? OwnedResult(
            InteropParameter parameter,
            StableId value
        ) {
            OwnershipContract? owned = parameter.ownership;
            if (owned is not { kind: NativeOwnershipKind.Owned, releaseFunction: not null, allocator: not null, releaseThreadDomain: "any" }
                || !Known(owned.evidence) || !Known(owned.lifetime.evidence) || owned.lifetime.kind != NativeLifetimeKind.Owner
                || owned.lifetime.owner != parameter.id
                || Resolve(value).category != NativeTypeCategory.Pointer) return null;
            int cleanup = Find(owned.releaseFunction.Value);
            if (cleanup < 0) return null;
            InteropFunction release = nativeExports[owned.releaseFunction.Value];
            if (release.asynchronous is not null || release.parameters.IsEmpty || Resolve(release.parameters[0].type).id != Resolve(value).id
                || release.parameters[0].ownership is not null || release.parameters[0].buffer is not null || release.parameters[0].text is not null) return null;
            if (release.error is null)
                return release.parameters.Length == 1 && Resolve(release.returnType).primitiveKind == "Void"
                    ? new(cleanup, owned.allocator.Value.value, -1, -1, 0) : null;
            ErrorContract error = release.error;
            if (!Known(error.evidence) || error.valueParameter is not null || error.errorParameter is not StableId errorId
                || error.errorOwnership is not { kind: NativeOwnershipKind.Owned, releaseFunction: not null } errorOwner || !Known(errorOwner.evidence)
                || errorOwner.releaseThreadDomain != "any"
                || !int.TryParse(error.successValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int success)
                || release.parameters.Length != 2 || IndexOf(release, errorId) != 1
                || Resolve(release.returnType) is not { category: NativeTypeCategory.Primitive, size: 4, isSigned: true }) return null;
            InteropType output = Resolve(release.parameters[1].type);
            if (release.parameters[1].ownership != errorOwner) return null;
            if (output.category != NativeTypeCategory.Pointer || output.elementType is not StableId errorPointerId) return null;
            InteropType pointer = Resolve(errorPointerId);
            if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId errorRecordId
                || Resolve(errorRecordId).category != NativeTypeCategory.Record) return null;
            int errorCleanup = Find(errorOwner.releaseFunction.Value);
            if (errorCleanup < 0) return null;
            InteropFunction errorRelease = nativeExports[errorOwner.releaseFunction.Value];
            if (errorRelease.error is not null || errorRelease.asynchronous is not null || errorRelease.parameters.Length != 1
                || Resolve(errorRelease.parameters[0].type).id != pointer.id || Resolve(errorRelease.returnType).primitiveKind != "Void") return null;
            return new(cleanup, owned.allocator.Value.value, 1, errorCleanup, success);
        }
        int Find(StableId export) => importIndices.TryGetValue(export, out int index) ? index : -1;
        static bool Known(Evidence evidence) => evidence.level is EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified && !string.IsNullOrWhiteSpace(evidence.origin);
        static int IndexOf(
            InteropFunction function,
            StableId id
        ) {
            for (int index = 0; index < function.parameters.Length; index++) if (function.parameters[index].id == id) return index;
            return -1;
        }
    }
}
