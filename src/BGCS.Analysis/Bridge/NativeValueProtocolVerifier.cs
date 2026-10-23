using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Checks every value accessor against actual final C carriers without interpreting source spellings.</summary>
public sealed class NativeValueProtocolVerifier
{
    /// <summary>Rejects incomplete, contradictory or invented value operation contracts.</summary>
    /// <param name="module">The immutable native ABI and its optional value protocols.</param>
    public void Verify(InteropModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Dictionary<StableId, InteropFunction> functions = module.functions.ToDictionary(function => function.id);
        HashSet<StableId> identities = [];
        foreach (InteropType holder in module.types.Where(type => type.valueProtocol is not null))
        {
            NativeValueProtocol protocol = holder.valueProtocol!;
            if (!Enum.IsDefined(protocol.family) || !identities.Add(protocol.id) || string.IsNullOrWhiteSpace(protocol.id.value)
                || string.IsNullOrWhiteSpace(protocol.sourceType.value) || holder.category != NativeTypeCategory.Record || holder.size is not null || !holder.fields.IsEmpty
                || protocol.storageType != holder.id || protocol.sourceType != holder.sourceNativeType || !Known(protocol.evidence)
                || protocol.operations.IsDefaultOrEmpty || protocol.elements.IsDefault)
                Fail("A value protocol needs unique provenance and its actual opaque C record.");
            bool sequence = protocol.family is NativeValueFamily.String or NativeValueFamily.Vector or NativeValueFamily.Span or NativeValueFamily.Array or NativeValueFamily.Map or NativeValueFamily.Set;
            bool duration = protocol.family is NativeValueFamily.Duration or NativeValueFamily.TimePoint;
            if (protocol.scopedRelease is NativeValueReleaseContract scoped && (protocol.family is not (NativeValueFamily.String or NativeValueFamily.Path)
                || protocol.releaseExport is null || scoped.scope != NativeValueReleaseScope.CreatingCall
                || scoped.evidence is not { level: EvidenceLevel.Declared or EvidenceLevel.Verified } || !Known(scoped.evidence)))
                Fail("A scoped value release requires exact standard string/path provenance and matched synchronous creating-call cleanup.");
            if ((protocol.text is not null) != (protocol.family == NativeValueFamily.String) || (protocol.sequence is not null) != sequence
                || (protocol.path is not null) != (protocol.family == NativeValueFamily.Path) || protocol.period is not null && !duration
                || (protocol.clock is not null) != (protocol.family == NativeValueFamily.TimePoint))
                Fail("Value family metadata does not match its finite protocol family.");
            int count = protocol.family switch { NativeValueFamily.Path => 0, NativeValueFamily.Map or NativeValueFamily.Expected => 2, NativeValueFamily.Variant => -1, _ => 1 };
            if (count >= 0 ? protocol.elements.Length != count : protocol.elements.IsEmpty) Fail("A value protocol lost its semantic element alternatives.");
            foreach (NativeValueElement element in protocol.elements)
                if (element is null || string.IsNullOrWhiteSpace(element.sourceType.value) || element.isVoid != (element.carrierType is null) || element.isVoid && element.isBoolean)
                    Fail("A value element needs an actual C carrier, except for an explicitly void alternative.");
                else if (element.carrierType is StableId carrier)
                {
                    InteropType value = Resolve(carrier);
                    if (element.isBoolean)
                    {
                        if (protocol.family is NativeValueFamily.UniqueOwner or NativeValueFamily.SharedOwner)
                        {
                            if (value.category != NativeTypeCategory.Pointer || value.elementType is null)
                                Fail("A native boolean owner needs its exact pointer carrier.");
                            value = Resolve(value.elementType!.Value);
                        }
                        Unsigned(value, 1);
                    }
                }
            if (protocol.text is NativeStringValueContract text)
            {
                InteropType unit = Resolve(protocol.elements[0].carrierType!.Value);
                if (!Known(text.evidence) || text.codeUnitWidth is not (1 or 2 or 4) || unit.size != text.codeUnitWidth || unit.category != NativeTypeCategory.Primitive
                    || unit.isSigned != text.isSigned || !Enum.IsDefined(text.byteOrder) || text.byteOrder != module.byteOrder
                    || !Enum.IsDefined(text.encoding) || text.lengthUnit != NativeLengthUnit.CodeUnits
                    || (text.zeroTerminated ? !ValidTerminated(text, protocol.sequence) : !text.permitsEmbeddedZero || text.maximumCodeUnits is not null || text.terminationEvidence is not null || !text.readOnlyReceiver)
                    || text.encoding != NativeTextEncoding.Unknown && (!Known(text.encodingEvidence)
                        || text.encoding == NativeTextEncoding.Utf8 && text.codeUnitWidth != 1
                        || text.encoding == NativeTextEncoding.Utf16 && text.codeUnitWidth != 2
                        || text.encoding == NativeTextEncoding.Utf32 && text.codeUnitWidth != 4))
                    Fail("String code units, byte order or encoding evidence contradict their actual C carrier.");
            }
            if (protocol.sequence is NativeSequenceValueContract storage && (!Known(storage.evidence)
                || storage.isBorrowedView != (protocol.family == NativeValueFamily.Span)
                || storage.isPackedBoolean && (protocol.family != NativeValueFamily.Vector || !protocol.elements[0].isBoolean)
                || protocol.evidence.level != EvidenceLevel.UserAsserted && storage.isPackedBoolean != (protocol.family == NativeValueFamily.Vector && protocol.elements[0].isBoolean)
                || storage.fixedLength is not null && protocol.family is not (NativeValueFamily.Array or NativeValueFamily.Span)
                || storage.readLifetime is null || !Enum.IsDefined(storage.readLifetime.kind)))
                Fail("Sequence extent, packed boolean or borrowed-view facts contradict the finite family.");
            if (protocol.path is NativePathValueContract path && (!Enum.IsDefined(path.format) || !Known(path.evidence)))
                Fail("The native path conversion needs its exact UTF-8 format policy.");
            if (protocol.period is NativeDurationPeriod period && (period.numerator <= 0 || period.denominator <= 0 || !Known(period.evidence)
                || BigInteger.GreatestCommonDivisor(period.numerator, period.denominator) != BigInteger.One))
                Fail("A native duration period needs normalized, positive, exact integral facts.");
            if (protocol.clock is NativeClockContract clock && (string.IsNullOrWhiteSpace(clock.identity.value) || !Known(clock.evidence)))
                Fail("A time point needs its detached original clock identity.");
            Dictionary<(NativeValueOperationKind Kind, int? Alternative), NativeValueExport> operations = [];
            HashSet<StableId> operationFunctions = [];
            foreach (NativeValueExport operation in protocol.operations)
            {
                if (operation is null || !Enum.IsDefined(operation.kind) || !operations.TryAdd((operation.kind, operation.alternativeIndex), operation)
                    || !operationFunctions.Add(operation.function) || operation.arguments.IsDefault || operation.arguments.Distinct().Count() != operation.arguments.Length
                    || !functions.TryGetValue(operation.function, out InteropFunction? function))
                    Fail("A native value operation duplicates or references a missing final C export.");
                function = functions[operation.function];
                if (!Allowed(protocol.family, operation.kind) || protocol.text?.zeroTerminated == true && operation.kind is NativeValueOperationKind.SetAt or NativeValueOperationKind.Append
                    || (operation.kind == NativeValueOperationKind.GetAlternative
                    ? operation.alternativeIndex is not int alternative || alternative < 0 || alternative >= protocol.elements.Length
                    : operation.alternativeIndex is not null))
                    Fail("A native value action does not belong to its family or variant alternative.");
                Boundary(function);
                ErrorContract channel = function.error!;
                InteropParameter[] actualInputs = function.parameters.Where(parameter => parameter.id != channel.errorParameter && parameter.id != channel.valueParameter).ToArray();
                StableId[] declaredInputs = operation.receiverParameter is StableId receiverId ? [receiverId, .. operation.arguments] : operation.arguments.ToArray();
                if (!actualInputs.Select(parameter => parameter.id).SequenceEqual(declaredInputs) || operation.arguments.Length != Arity(operation.kind)
                    || (operation.receiverParameter is null) != (operation.kind == NativeValueOperationKind.Construct))
                    Fail("A native value operation changed its complete ordered C input coverage.");
                if (operation.receiverParameter is StableId self)
                {
                    InteropParameter receiver = actualInputs[0];
                    InteropType pointer = Resolve(receiver.type);
                    bool mutates = operation.kind is NativeValueOperationKind.SetAt or NativeValueOperationKind.Append or NativeValueOperationKind.Insert or NativeValueOperationKind.MoveOwner;
                    if (receiver.id != self || pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId element
                        || Resolve(element).id != holder.id || Const(element) != (!mutates && protocol.text?.readOnlyReceiver != false)
                        || receiver.ownership is not { kind: NativeOwnershipKind.Borrowed, lifetime.kind: NativeLifetimeKind.Call } borrowed
                        || !Known(borrowed.evidence) || !Known(borrowed.lifetime.evidence))
                        Fail("A value receiver lost its const-qualified borrowed holder contract.");
                }
                InteropType? result = Result(function);
                bool noResult = operation.kind is NativeValueOperationKind.SetAt or NativeValueOperationKind.Append or NativeValueOperationKind.Insert;
                if (noResult != (result is null)) Fail("A value operation changed its result-out presence.");
                if (operation.kind is NativeValueOperationKind.Count or NativeValueOperationKind.Discriminator or NativeValueOperationKind.PathUtf8Count or NativeValueOperationKind.CopyCodeUnits)
                    Unsigned(result!, 8);
                if (operation.kind == NativeValueOperationKind.CopyCodeUnits)
                {
                    InteropType destination = Resolve(function.parameters.Single(parameter => parameter.id == operation.arguments[0]).type);
                    Unsigned(Resolve(function.parameters.Single(parameter => parameter.id == operation.arguments[1]).type), 8);
                    if (destination.category != NativeTypeCategory.Pointer || destination.elementType is not StableId destinationUnit || Const(destinationUnit))
                        Fail("A code-unit copy requires its exact writable C destination pointer.");
                    InteropType unit = Resolve(destination.elementType!.Value);
                    if (protocol.family == NativeValueFamily.Path) Unsigned(unit, 1);
                    else if (protocol.elements[0].carrierType is not StableId carrier || Resolve(carrier).id != unit.id
                        || unit.category != NativeTypeCategory.Primitive || unit.size is not (1 or 2 or 4 or 8) || unit.isSigned is null)
                        Fail("A code-unit copy changed its original integral string element carrier.");
                }
                if (operation.kind is NativeValueOperationKind.HasValue or NativeValueOperationKind.PathUtf8Byte) Unsigned(result!, 1);
                if (operation.kind is NativeValueOperationKind.GetAt or NativeValueOperationKind.SetAt or NativeValueOperationKind.GetKeyAt or NativeValueOperationKind.GetMappedAt or NativeValueOperationKind.PathUtf8Byte)
                    Unsigned(Resolve(function.parameters.Single(parameter => parameter.id == operation.arguments[0]).type), 8);
                int elementIndex = operation.kind switch
                {
                    NativeValueOperationKind.GetAt or NativeValueOperationKind.GetKeyAt or NativeValueOperationKind.GetValue or NativeValueOperationKind.DurationCount
                        or NativeValueOperationKind.TimePointCount or NativeValueOperationKind.GetPointer => 0,
                    NativeValueOperationKind.GetMappedAt or NativeValueOperationKind.GetError => 1,
                    NativeValueOperationKind.GetAlternative => operation.alternativeIndex!.Value,
                    _ => -1
                };
                if (elementIndex >= 0 && (protocol.elements[elementIndex].carrierType is not StableId expected || result!.id != Resolve(expected).id))
                    Fail("A value operation changed its established copied-element carrier.");
                if (operation.kind is NativeValueOperationKind.SetAt or NativeValueOperationKind.Append)
                    CheckInput(operation.arguments[^1], 0);
                if (operation.kind == NativeValueOperationKind.Insert) { CheckInput(operation.arguments[0], 0); CheckInput(operation.arguments[1], 1); }
                if (operation.kind is NativeValueOperationKind.Construct or NativeValueOperationKind.CloneOwner or NativeValueOperationKind.MoveOwner)
                {
                    InteropParameter output = function.parameters.Single(parameter => parameter.id == channel.valueParameter);
                    if (result!.category != NativeTypeCategory.Pointer || result.elementType is not StableId element || Resolve(element).id != holder.id
                        || output.ownership is not { kind: NativeOwnershipKind.Owned, releaseFunction: not null, allocator: not null } owned
                        || owned.releaseFunction != protocol.releaseExport || !Known(owned.evidence))
                        Fail("A fresh native holder lost its independently matched owned-result cleanup contract.");
                }
                void CheckInput(
                    StableId parameterId,
                    int index
                ) {
                    InteropType expected = Resolve(protocol.elements[index].carrierType!.Value);
                    InteropType actual = Resolve(function.parameters.Single(parameter => parameter.id == parameterId).type);
                    if (actual.id != expected.id && !(expected.category == NativeTypeCategory.Record && expected.size is not null
                        && actual.category == NativeTypeCategory.Pointer && actual.elementType is StableId element && Resolve(element).id == expected.id))
                        Fail("A native value mutation changed its copied-element C input carrier.");
                }
            }
            foreach (NativeValueOperationKind required in Required(protocol.family))
                if (!operations.ContainsKey((required, null))) Fail("A native value protocol omitted a required checked operation.");
            if (protocol.scopedRelease is not null && !operations.ContainsKey((NativeValueOperationKind.CopyCodeUnits, null)))
                Fail("A creating-call string/path release requires its complete bounded code-unit copy operation.");
            for (int index = 0; index < protocol.elements.Length; index++)
            {
                NativeValueOperationKind action = protocol.family switch
                {
                    NativeValueFamily.Map => index == 0 ? NativeValueOperationKind.GetKeyAt : NativeValueOperationKind.GetMappedAt,
                    NativeValueFamily.Expected => index == 0 ? NativeValueOperationKind.GetValue : NativeValueOperationKind.GetError,
                    NativeValueFamily.Optional => NativeValueOperationKind.GetValue,
                    NativeValueFamily.Variant => NativeValueOperationKind.GetAlternative,
                    _ => NativeValueOperationKind.GetAt
                };
                if (protocol.family is NativeValueFamily.Optional or NativeValueFamily.Expected or NativeValueFamily.Variant
                    && operations.ContainsKey((action, protocol.family == NativeValueFamily.Variant ? index : null)) == protocol.elements[index].isVoid)
                    Fail("A native alternative lost its required copy operation or invented one for void.");
            }
            if (protocol.releaseExport is null) continue; // A borrowed input-only holder does not grant ownership or require a destructor export.
            if (protocol.releaseExport is not StableId releaseId || !functions.TryGetValue(releaseId, out InteropFunction? release)
                || release.error is not { valueParameter: null } releaseError || release.parameters.Length != 2
                || release.parameters[0].id == releaseError.errorParameter || Resolve(release.parameters[0].type) is not { category: NativeTypeCategory.Pointer, elementType: not null } releasePointer
                || Resolve(releasePointer.elementType.Value).id != holder.id || Const(releasePointer.elementType.Value))
                Fail("A native value protocol lost its exact holder destruction export.");
            Boundary(functions[protocol.releaseExport!.Value]);
        }

        InteropType Resolve(StableId id)
        {
            HashSet<StableId> visited = [];
            InteropType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visited.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("A native value carrier has a missing or cyclic alias chain.");
                type = module.GetType(element);
            }
            return type;
        }
        bool Const(StableId id)
        {
            HashSet<StableId> visited = [];
            while (visited.Add(id))
            {
                InteropType type = module.GetType(id);
                if (type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true) return true;
                if (type.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified) || type.elementType is not StableId element) return false;
                id = element;
            }
            throw new InvalidOperationException("A native value qualification chain is cyclic.");
        }
        InteropType? Result(InteropFunction function)
        {
            if (function.error?.valueParameter is not StableId id) return null;
            InteropType pointer = Resolve(function.parameters.Single(parameter => parameter.id == id).type);
            if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId element)
                throw new InvalidOperationException("A native value result must retain its actual C output pointer.");
            return Resolve(element);
        }
        void Boundary(InteropFunction function)
        {
            if (function.isVariadic || !function.hasPrototype || function.callingConvention != "C" || function.asynchronous is not null
                || function.error is not { errorParameter: not null, errorOwnership: not null } error || !Known(error.evidence) || error.successValue != "0"
                || function.parameters.IsEmpty || function.parameters[^1].id != error.errorParameter
                || Resolve(function.returnType) is not { category: NativeTypeCategory.Primitive, size: 4, isSigned: true })
                Fail("A value accessor needs its exact fixed status and owned error boundary.");
            InteropParameter output = function.parameters[^1];
            OwnershipContract owned = function.error!.errorOwnership!;
            if (output.ownership != owned || owned is not { kind: NativeOwnershipKind.Owned, releaseFunction: not null, allocator: not null }
                || !Known(owned.evidence) || !Known(owned.lifetime.evidence) || owned.lifetime.kind != NativeLifetimeKind.Owner || owned.lifetime.owner != output.id
                || Resolve(output.type) is not { category: NativeTypeCategory.Pointer, elementType: not null } errorOutput
                || Resolve(errorOutput.elementType.Value) is not { category: NativeTypeCategory.Pointer, elementType: not null } errorPointer
                || Resolve(errorPointer.elementType.Value).category != NativeTypeCategory.Record
                || !functions.TryGetValue(owned.releaseFunction.Value, out InteropFunction? cleanup) || cleanup.error is not null || cleanup.isVariadic
                || !cleanup.hasPrototype || cleanup.callingConvention != "C" || cleanup.parameters.Length != 1
                || Resolve(cleanup.parameters[0].type).id != errorPointer.id || Resolve(cleanup.returnType).primitiveKind != "Void")
                Fail("A native value error channel lost its owned allocation or matching C cleanup.");
        }
        static void Unsigned(
            InteropType type,
            int width
        ) {
            if (type.category != NativeTypeCategory.Primitive || type.size != width || type.isSigned != false) Fail("A native value count, flag or index changed its fixed unsigned C scalar.");
        }
    }

    private static int Arity(NativeValueOperationKind kind) => kind switch
    {
        NativeValueOperationKind.GetAt or NativeValueOperationKind.Append or NativeValueOperationKind.GetKeyAt or NativeValueOperationKind.GetMappedAt or NativeValueOperationKind.PathUtf8Byte => 1,
        NativeValueOperationKind.SetAt or NativeValueOperationKind.Insert or NativeValueOperationKind.CopyCodeUnits => 2,
        _ => 0
    };
    private static bool Allowed(
        NativeValueFamily family,
        NativeValueOperationKind action
    ) => action switch
    {
        NativeValueOperationKind.Construct => family is not (NativeValueFamily.Span or NativeValueFamily.Path or NativeValueFamily.Duration or NativeValueFamily.TimePoint),
        NativeValueOperationKind.Count => family is NativeValueFamily.String or NativeValueFamily.Vector or NativeValueFamily.Span or NativeValueFamily.Array or NativeValueFamily.Map or NativeValueFamily.Set,
        NativeValueOperationKind.GetAt => family is NativeValueFamily.String or NativeValueFamily.Vector or NativeValueFamily.Span or NativeValueFamily.Array or NativeValueFamily.Set,
        NativeValueOperationKind.SetAt => family is NativeValueFamily.String or NativeValueFamily.Vector or NativeValueFamily.Span or NativeValueFamily.Array,
        NativeValueOperationKind.Append => family is NativeValueFamily.String or NativeValueFamily.Vector or NativeValueFamily.Set,
        NativeValueOperationKind.GetKeyAt or NativeValueOperationKind.GetMappedAt or NativeValueOperationKind.Insert => family == NativeValueFamily.Map,
        NativeValueOperationKind.HasValue => family is NativeValueFamily.Optional or NativeValueFamily.Expected or NativeValueFamily.UniqueOwner or NativeValueFamily.SharedOwner,
        NativeValueOperationKind.GetValue => family is NativeValueFamily.Optional or NativeValueFamily.Expected,
        NativeValueOperationKind.GetError => family == NativeValueFamily.Expected,
        NativeValueOperationKind.Discriminator or NativeValueOperationKind.GetAlternative => family == NativeValueFamily.Variant,
        NativeValueOperationKind.GetPointer => family is NativeValueFamily.UniqueOwner or NativeValueFamily.SharedOwner,
        NativeValueOperationKind.CloneOwner => family == NativeValueFamily.SharedOwner,
        NativeValueOperationKind.MoveOwner => family == NativeValueFamily.UniqueOwner,
        NativeValueOperationKind.PathUtf8Count or NativeValueOperationKind.PathUtf8Byte => family == NativeValueFamily.Path,
        NativeValueOperationKind.DurationCount => family == NativeValueFamily.Duration,
        NativeValueOperationKind.TimePointCount => family == NativeValueFamily.TimePoint,
        NativeValueOperationKind.CopyCodeUnits => family is NativeValueFamily.String or NativeValueFamily.Path,
        _ => false
    };
    private static NativeValueOperationKind[] Required(NativeValueFamily family) => family switch
    {
        NativeValueFamily.String or NativeValueFamily.Vector or NativeValueFamily.Span or NativeValueFamily.Array or NativeValueFamily.Set => [NativeValueOperationKind.Count, NativeValueOperationKind.GetAt],
        NativeValueFamily.Map => [NativeValueOperationKind.Count, NativeValueOperationKind.GetKeyAt, NativeValueOperationKind.GetMappedAt],
        NativeValueFamily.Optional or NativeValueFamily.Expected => [NativeValueOperationKind.HasValue],
        NativeValueFamily.Variant => [NativeValueOperationKind.Discriminator],
        NativeValueFamily.Path => [NativeValueOperationKind.PathUtf8Count, NativeValueOperationKind.PathUtf8Byte],
        NativeValueFamily.Duration => [NativeValueOperationKind.DurationCount],
        NativeValueFamily.TimePoint => [NativeValueOperationKind.TimePointCount],
        NativeValueFamily.UniqueOwner => [NativeValueOperationKind.HasValue, NativeValueOperationKind.GetPointer, NativeValueOperationKind.MoveOwner],
        NativeValueFamily.SharedOwner => [NativeValueOperationKind.HasValue, NativeValueOperationKind.GetPointer, NativeValueOperationKind.CloneOwner],
        _ => []
    };
    private static bool ValidTerminated(
        NativeStringValueContract text,
        NativeSequenceValueContract? sequence
    ) => !text.permitsEmbeddedZero
        && text.encoding == NativeTextEncoding.Utf8 && text.codeUnitWidth == 1 && text.maximumCodeUnits is > 0
        && text.terminationEvidence is { level: EvidenceLevel.UserAsserted } termination && Known(termination)
        && sequence?.readLifetime is { kind: NativeLifetimeKind.Call, owner: null, evidence.level: EvidenceLevel.UserAsserted } lifetime && Known(lifetime.evidence);

    private static bool Known(Evidence? evidence) => evidence is { level: EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified }
        && !string.IsNullOrWhiteSpace(evidence.origin);
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string message) => throw new InvalidOperationException(message);
}
