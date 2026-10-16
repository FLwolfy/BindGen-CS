using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Bridge;
using BGCS.Analysis.Safety;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;

namespace BGCS.CSharp.Planning;

internal static class ValueStringResultPlanner
{
    internal static CSharpEmissionPlan Apply(
        CSharpEmissionPlan plan,
        EmissionRequest request
    ) {
        if (request.maximumValueStringCodeUnits <= 0) throw new ArgumentOutOfRangeException(nameof(request.maximumValueStringCodeUnits));
        if (plan.api.interop.types.All(type => type.valueProtocol is null)) return plan;
        new NativeValueProtocolVerifier().Verify(plan.api.interop);
        var results = ImmutableArray.CreateBuilder<CSharpValueStringResultPlan>();
        var omissions = plan.ownedProjectionOmissions.ToBuilder();
        for (int index = 0; index < plan.methods.Length; index++)
        {
            InteropFunction function = Function(index);
            if (function.error?.valueParameter is not StableId valueId) continue;
            InteropParameter result = function.parameters.Single(parameter => parameter.id == valueId);
            InteropType output = Resolve(result.type);
            if (output.category != NativeTypeCategory.Pointer || output.elementType is not StableId addressId) continue;
            InteropType address = Resolve(addressId);
            if (address.category != NativeTypeCategory.Pointer || address.elementType is not StableId holderId) continue;
            NativeValueProtocol? protocol = Resolve(holderId).valueProtocol;
            if (protocol?.family is not (NativeValueFamily.String or NativeValueFamily.Path)) continue;
            // Borrowed holders are not fresh values and cannot be destroyed by a copying convenience.
            if (result.ownership?.kind != NativeOwnershipKind.Owned) continue;
            try { results.Add(Plan(index, function, result, address, protocol)); }
            catch (NotSupportedException failure) { omissions.Add(new(function.id, result.id, function.source, failure.Message)); }
        }
        var replaced = results.Select(result => result.factory.methodIndex).ToHashSet();
        return plan with
        {
            valueStringResults = results.ToImmutable(),
            statusMethods = plan.statusMethods.Where(method => !replaced.Contains(method.methodIndex)).ToImmutableArray(),
            ownedProjectionOmissions = omissions.ToImmutable()
        };

        CSharpValueStringResultPlan Plan(
            int index,
            InteropFunction function,
            InteropParameter output,
            InteropType address,
            NativeValueProtocol protocol
        ) {
            if (protocol.scopedRelease is not { scope: NativeValueReleaseScope.CreatingCall } scope || !Known(scope.evidence)
                || protocol.releaseExport is not StableId releaseId || output.ownership is not { lifetime.kind: NativeLifetimeKind.Owner, releaseFunction: StableId ownedRelease, allocator: not null } owned
                || ownedRelease != releaseId || owned.lifetime.owner != output.id || !Known(owned.evidence) || !Known(owned.lifetime.evidence))
                Reject("A native text value needs fresh owned storage and an explicit same-creating-call cleanup capability; arbitrary cleanup affinity is not inferred.");
            NativeTextEncoding encoding = protocol.family == NativeValueFamily.Path ? NativeTextEncoding.Utf8 : protocol.text!.encoding;
            int width = protocol.family == NativeValueFamily.Path ? 1 : protocol.text!.codeUnitWidth;
            if (encoding is not (NativeTextEncoding.Utf8 or NativeTextEncoding.Utf16 or NativeTextEncoding.Utf32)
                || protocol.family == NativeValueFamily.String && !Known(protocol.text!.encodingEvidence))
                Reject("A native string value requires explicit Unicode encoding evidence; char storage does not imply UTF-8.");
            NativeTextByteOrder order = plan.api.interop.byteOrder switch
            { NativeByteOrder.LittleEndian => NativeTextByteOrder.LittleEndian, NativeByteOrder.BigEndian => NativeTextByteOrder.BigEndian, _ => NativeTextByteOrder.Unknown };
            if (width > 1 && order == NativeTextByteOrder.Unknown) Reject("A multibyte native text value needs known code-unit byte order.");
            CSharpScopedStatusCallPlan factory = Status(index);
            NativeValueOperationKind countKind = protocol.family == NativeValueFamily.Path ? NativeValueOperationKind.PathUtf8Count : NativeValueOperationKind.Count;
            CSharpScopedStatusCallPlan count = Status(Import(protocol.operations.Single(operation => operation.kind == countKind).function));
            NativeValueExport? copyOperation = protocol.operations.SingleOrDefault(operation => operation.kind == NativeValueOperationKind.CopyCodeUnits);
            if (copyOperation is null) Reject("A native text copy needs an exact bounded bulk-copy operation rather than repeated conversions or borrowed temporary pointers.");
            CSharpScopedStatusCallPlan copy = Status(Import(copyOperation!.function));
            CSharpScopedStatusCallPlan release = Status(Import(protocol.releaseExport!.Value));
            if (factory.resultType != plan.methods[index].parameterTypes[factory.resultIndex][..^1].TrimEnd()
                || count.resultType != "ulong" || copy.resultType != "ulong" || release.resultIndex != -1
                || count.inputIndices.Length != 1 || copy.inputIndices.Length != 3 || release.inputIndices.Length != 1)
                Reject("A native text copy changed its exact holder/count/destination/capacity/release ABI.");
            InteropFunction countFunction = Function(count.methodIndex), copyFunction = Function(copy.methodIndex), releaseFunction = Function(release.methodIndex);
            foreach ((InteropFunction accessor, int receiver) in new[] { (countFunction, count.inputIndices[0]), (copyFunction, copy.inputIndices[0]), (releaseFunction, release.inputIndices[0]) })
            {
                InteropType receiverType = Resolve(accessor.parameters[receiver].type);
                if (receiverType.category != NativeTypeCategory.Pointer || receiverType.elementType is not StableId receiverHolder || Resolve(receiverHolder).id != Resolve(address.elementType!.Value).id)
                    Reject("A native text operation must use the identical owned holder, including its exact source storage identity.");
            }
            int alignment = new FriendlyApiEligibilityAnalyzer().GetTextInputAlignment(plan.api.interop, copyFunction.parameters[copy.inputIndices[1]]);
            if (alignment > 8) Reject("An over-aligned native text copy needs an explicit aligned destination adapter.");
            CSharpMethodPlan method = plan.methods[index];
            int[] ownedSelf = factory.inputIndices.Where(input => OwnedReceiver(function, function.parameters[input])).ToArray();
            if (!factory.inputIndices.All(input => OverloadPlanner.SupportsDirectInput(plan, method, function, input) || BorrowedAddress(function.parameters[input])
                || ownedSelf.Length == 1 && ownedSelf[0] == input))
                Reject("A native text factory input lacks a supported nonretaining call protocol; its exact Raw factory remains available.");
            int[] visible = OverloadPlanner.PublicInputs(method, factory.inputIndices).ToArray();
            CSharpFacadeMethodPlan signature = new(method.name, "string", visible.Select(input => OverloadPlanner.InputType(method, input)).ToImmutableArray(),
                visible.Select(input => method.parameterNames[input]).ToImmutableArray())
            {
                nativeExport = method.nativeExport,
                inputIndices = visible.ToImmutableArray(),
                parameterModifiers = visible.Select(input => OverloadPlanner.InputModifier(method, input)).ToImmutableArray(),
                documentation = method.documentation,
                documentationContent = method.documentationContent is { } documentation ? documentation with { parameters = visible.Select(input => documentation.parameters[input]).ToImmutableArray() } : null
            };
            return new(factory, count, copy, release, signature, encoding, width, order, request.maximumValueStringCodeUnits, alignment)
            { requiresOwnedReceiver = factory.inputIndices.Any(input => !OverloadPlanner.SupportsDirectInput(plan, method, function, input) && !BorrowedAddress(function.parameters[input])) };
        }

        CSharpScopedStatusCallPlan Status(int index)
        {
            InteropFunction function = Function(index);
            if (function.asynchronous is not null || function.isVariadic || !function.hasPrototype || function.callingConvention != "C" || function.returnOwnership is not null || function.returnString is not null
                || function.error is not { errorParameter: StableId errorId, successValue: "0", errorOwnership: not null } error || !Known(error.evidence)
                || Resolve(function.returnType) is not { category: NativeTypeCategory.Primitive, size: 4, isSigned: true })
                Reject("A scoped native text operation requires a fixed synchronous int32 status with an exact owned error channel.");
            ErrorContract channel = function.error!;
            int errorIndex = Index(function, channel.errorParameter!.Value);
            OwnershipContract ownership = channel.errorOwnership!;
            if (ownership is not { kind: NativeOwnershipKind.Owned, releaseFunction: StableId, allocator: not null, releaseThreadDomain: "any", lifetime.kind: NativeLifetimeKind.Owner }
                || !Known(ownership.evidence) || !Known(ownership.lifetime.evidence) || ownership.lifetime.owner != function.parameters[errorIndex].id
                || function.parameters[errorIndex].ownership != ownership || Resolve(function.parameters[errorIndex].type) is not { category: NativeTypeCategory.Pointer, elementType: StableId })
                Reject("A scoped native text error must retain its matching nonthrowing error-release domain.");
            InteropType errorPointer = Resolve(Resolve(function.parameters[errorIndex].type).elementType!.Value);
            int cleanup = Import(ownership.releaseFunction!.Value);
            InteropFunction release = Function(cleanup);
            if (errorPointer.category != NativeTypeCategory.Pointer || errorPointer.elementType is not StableId errorRecord || Resolve(errorRecord).category != NativeTypeCategory.Record
                || release.parameters.Length != 1 || Resolve(release.parameters[0].type).id != errorPointer.id || Resolve(release.returnType).primitiveKind != "Void"
                || release.error is not null || release.asynchronous is not null || release.isVariadic || !release.hasPrototype || release.callingConvention != "C"
                || release.parameters[0].ownership is not null || release.parameters[0].text is not null || release.parameters[0].buffer is not null || release.parameters[0].callback is not null || release.parameters[0].singleValue is not null)
                Reject("A scoped native text error cleanup changed its exact void(error*) ABI.");
            int result = channel.valueParameter is StableId resultId ? Index(function, resultId) : -1;
            if (result == errorIndex || result >= 0 && Resolve(function.parameters[result].type).category != NativeTypeCategory.Pointer)
                Reject("A scoped native text result must be separate output storage.");
            return new(index, Enumerable.Range(0, function.parameters.Length).Where(input => input != errorIndex && input != result).ToImmutableArray(), result,
                result < 0 ? "void" : plan.methods[index].parameterTypes[result][..^1].TrimEnd(), errorIndex, cleanup);
        }

        bool BorrowedAddress(InteropParameter parameter) => parameter.text is null && parameter.buffer is null && parameter.callback is null && parameter.singleValue is null
            && parameter.ownership is { kind: NativeOwnershipKind.Borrowed, lifetime.kind: NativeLifetimeKind.Call, retainsPointer: false } ownership
            && Known(ownership.evidence) && Known(ownership.lifetime.evidence) && Resolve(parameter.type).category == NativeTypeCategory.Pointer;
        bool OwnedReceiver(
            InteropFunction function,
            InteropParameter parameter
        ) {
            if (function.sourceOwnerType is not StableId owner || parameter.text is not null || parameter.buffer is not null || parameter.callback is not null || parameter.singleValue is not null
                || parameter.ownership is not { kind: NativeOwnershipKind.Borrowed, lifetime.kind: NativeLifetimeKind.Call, retainsPointer: null } borrowed
                || !Known(borrowed.evidence) || !Known(borrowed.lifetime.evidence)
                || Resolve(parameter.type) is not { category: NativeTypeCategory.Pointer, elementType: StableId element } || Resolve(element).sourceNativeType != owner) return false;
            CSharpStatusMethodPlan[] creators = plan.statusMethods.Where(status => status.ownedResult is not null && status.resultIndex >= 0
                && Resolve(Function(status.methodIndex).parameters[status.resultIndex].type) is { elementType: StableId output }
                && Resolve(output) is { category: NativeTypeCategory.Pointer, elementType: StableId record } && Resolve(record).sourceNativeType == owner).ToArray();
            return creators.Length != 0 && creators.All(status => NativeReceiverTypeMatcher.CanBorrowWithoutRemovingQualifiers(plan.api.interop,
                Resolve(Function(status.methodIndex).parameters[status.resultIndex].type).elementType!.Value, parameter.type));
        }
        InteropFunction Function(int index) => plan.api.interop.functions.Single(function => function.id == plan.methods[index].nativeExport);
        int Import(StableId id)
        {
            for (int index = 0; index < plan.methods.Length; index++) if (plan.methods[index].nativeExport == id) return index;
            throw new NotSupportedException("A native text value requires every exact copy and cleanup import to be selected.");
        }
        static int Index(
            InteropFunction function,
            StableId id
        ) {
            for (int index = 0; index < function.parameters.Length; index++) if (function.parameters[index].id == id) return index;
            throw new NotSupportedException("A native text operation references a missing result or error parameter.");
        }
        InteropType Resolve(StableId id)
        {
            HashSet<StableId> seen = [];
            InteropType type = plan.api.interop.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!seen.Add(type.id) || type.elementType is not StableId next) throw new InvalidOperationException("A native text value type graph is cyclic or incomplete.");
                type = plan.api.interop.GetType(next);
            }
            return type;
        }
    }

    private static bool Known(Evidence evidence) => evidence.level is EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified && !string.IsNullOrWhiteSpace(evidence.origin);
    [System.Diagnostics.CodeAnalysis.DoesNotReturn] private static void Reject(string message) => throw new NotSupportedException(message);
}
