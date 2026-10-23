using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Interop;
using BGCS.Analysis.Types;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

internal sealed class CPointerBridgeVerifier
{
    internal InteropModule VerifyAndMerge(
        NativeBridgePlan plan,
        NativeModule reparsed,
        string libraryId
    ) {
        new CBridgeOperationVerifier().Verify(plan);
        if (plan.source.targetId != reparsed.targetId || plan.source.triple != reparsed.triple
            || plan.source.pointerSize != reparsed.pointerSize || plan.source.byteOrder != reparsed.byteOrder)
            throw new InvalidOperationException("The C pointer bridge was parsed for a different native target.");
        Dictionary<StableId, BridgeType> types = plan.types.ToDictionary(type => type.id);
        HashSet<(StableId source, StableId actual)> checkedStorage = [];
        Dictionary<StableId, StableId> sourceRecordTypes = [];
        Dictionary<StableId, StableId> semanticTypes = [];
        ILookup<string, NativeDeclaration> reparsedFunctions = reparsed.declarations
            .Where(item => item.kind == "Function").ToLookup(item => item.name, StringComparer.Ordinal);
        NativeDeclaration[] declarations = plan.exports.Select(export =>
        {
            NativeDeclaration[] matches = reparsedFunctions[export.name].ToArray();
            if (matches.Length != 1 || !matches[0].hasPrototype || matches[0].isVariadic || matches[0].isStatic
                || matches[0].callingConvention is not ("C" or "Default"))
                throw new InvalidOperationException("A C pointer bridge export has a missing or changed physical prototype.");
            return matches[0];
        }).ToArray();
        InteropModule physical = new CDeclarationAnalyzer().Analyze(reparsed, libraryId, declarations.Select(item => item.id));
        if (physical.diagnostics.Any(item => item.isFailure)) throw new InvalidOperationException("The C bridge failed static ABI analysis.");
        InteropModule original = plan.sourceSemantics ?? new CDeclarationAnalyzer().Analyze(plan.source, libraryId,
            plan.exports.Select(export => export.operation.declaration!.Value), CDeclarationAnalysisPurpose.NativeBridgePlanning);
        ILookup<string, InteropFunction> physicalFunctions = physical.functions.ToLookup(function => function.exportName, StringComparer.Ordinal);
        Dictionary<StableId, InteropFunction> originalFunctions = original.functions.ToDictionary(function => function.id);
        ImmutableArray<InteropFunction>.Builder functions = ImmutableArray.CreateBuilder<InteropFunction>();
        foreach (BridgeExport export in plan.exports)
        {
            if (export.usesErrorChannel || export.operation.kind is not (BridgeOperationKind.FreeCall or BridgeOperationKind.FiniteVariadicCall))
                throw new NotSupportedException("A C pointer bridge requires an exact typed C operation.");
            InteropFunction actual = physicalFunctions[export.name].Single();
            NativeVariadicAdapter? finite = export.operation.finiteVariadic;
            if (!originalFunctions.TryGetValue(export.operation.declaration!.Value, out InteropFunction? source))
                source = new CDeclarationAnalyzer().Analyze(plan.source, libraryId, [export.operation.declaration.Value], CDeclarationAnalysisPurpose.NativeBridgePlanning).functions.Single();
            int expectedCount = export.parameters.Length + (export.resultType is not null && export.resultInOutputParameter ? 1 : 0);
            if (actual.parameters.Length != expectedCount)
                throw new InvalidOperationException("A C bridge changed its input or output channel count.");
            if (export.resultType is StableId returnType && !export.resultInOutputParameter)
                Check(returnType, actual.returnType);
            else if (Resolve(reparsed, actual.returnType).primitiveKind != "Void")
                throw new InvalidOperationException("An output-pointer C bridge unexpectedly returns a native value.");
            ImmutableArray<InteropParameter>.Builder parameters = ImmutableArray.CreateBuilder<InteropParameter>();
            for (int index = 0; index < export.parameters.Length; index++)
            {
                BridgeParameter expected = export.parameters[index];
                InteropParameter actualParameter = actual.parameters[index];
                BridgeArgumentGroup? group = export.operation.argumentGroups.IsEmpty ? null : export.operation.argumentGroups.Single(group => group.inputs.Any(input => input.parameter == expected.id));
                InteropParameter sourceParameter = group is not null ? source.parameters.Single(parameter => parameter.id == group.sourceParameter)
                    : index < source.parameters.Length ? source.parameters[index] : new(expected.id, expected.name, actualParameter.type);
                bool expanded = group is { expression: not null };
                if (expanded && (sourceParameter.callback is not null || sourceParameter.ownership is not null || sourceParameter.text is not null
                    || sourceParameter.buffer is not null || sourceParameter.singleValue is not null))
                    throw new NotSupportedException("An expanded C carrier needs explicit semantics rather than reusing a source pointer-lifetime protocol.");
                Check(expected.type, actualParameter.type);
                Evidence evidence = new(EvidenceLevel.Declared, "native-bridge:" + export.id.value);
                CallbackContract? callback = sourceParameter.callback;
                if (callback is not null)
                {
                    NativeType function = Resolve(reparsed, actualParameter.type);
                    if (function.category == NativeTypeCategory.Pointer) function = Resolve(reparsed, function.elementType!.Value);
                    AggregateCallbackAdapter? adapter = plan.aggregateCallbacks.SingleOrDefault(adapter => adapter.sourceFunction == source.id && adapter.sourceParameter == sourceParameter.id);
                    bool resultOut = adapter is not null && Resolve(plan.source, plan.source.GetType(adapter.sourceCallableType).returnType!.Value).category == NativeTypeCategory.Record;
                    callback = callback with { callableType = function.id, requiredResultOutputIndex = resultOut ? function.parameterTypes.Length - 1 : callback.requiredResultOutputIndex };
                }
                parameters.Add(sourceParameter with
                {
                    id = expanded || finite is not null ? expected.id : sourceParameter.id,
                    name = expanded ? expected.name : sourceParameter.name,
                    type = actualParameter.type,
                    sourceDeclaration = finite is not null && index >= source.parameters.Length ? null : sourceParameter.id,
                    sourceName = expanded ? expected.name : sourceParameter.name,
                    callback = callback,
                    singleValue = expected.conversion == BridgeConversionKind.Dereference
                        ? new(NativeParameterDirection.Input, new(NativeLifetimeKind.Call, null, evidence), evidence)
                        {
                            retainsPointer = false,
                            allowsNull = false,
                            copySource = Resolve(plan.source, expected.nativeType!.Value).category == NativeTypeCategory.Record
                                ? new(export.id, source.sourceDeclaration ?? source.id, index < source.parameters.Length ? sourceParameter.id : null,
                                    Resolve(plan.source, expected.nativeType.Value).id, Resolve(reparsed, Resolve(reparsed, actualParameter.type).elementType!.Value).id,
                                    NativeParameterDirection.Input, evidence) : null
                        } : sourceParameter.singleValue
                });
            }
            if (export.resultType is StableId result && export.resultInOutputParameter)
            {
                InteropParameter output = actual.parameters[^1];
                NativeType pointer = Resolve(reparsed, output.type);
                if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId element)
                    throw new InvalidOperationException("A C aggregate result lost its typed output pointer.");
                Check(result, element);
                if (source.returnOwnership is not null || source.returnString is not null)
                    throw new NotSupportedException("An owned or text aggregate result requires an explicit semantic callable lowering.");
                Evidence evidence = new(EvidenceLevel.Declared, "native-bridge-result:" + export.id.value);
                parameters.Add(output with
                {
                    singleValue = new(NativeParameterDirection.Output, new(NativeLifetimeKind.Call, null, evidence), evidence)
                    {
                        allowsNull = false,
                        retainsPointer = false,
                        copySource = Resolve(plan.source, export.nativeResultType!.Value).category == NativeTypeCategory.Record
                            ? new(export.id, source.sourceDeclaration ?? source.id, null, Resolve(plan.source, export.nativeResultType.Value).id, Resolve(reparsed, element).id, NativeParameterDirection.Output, evidence) : null
                    }
                });
            }
            functions.Add(source with
            {
                id = finite is null ? source.id : export.id,
                nativeBridgeExport = export.id,
                exportName = actual.exportName,
                returnType = actual.returnType,
                sourceDeclaration = source.sourceDeclaration ?? source.id,
                sourceName = finite?.wrapper ?? source.sourceName,
                sourceQualifiedName = finite?.wrapper ?? source.sourceQualifiedName,
                sourceSignature = finite?.sourceSignature ?? source.sourceSignature,
                isVariadic = false,
                hasPrototype = true,
                parameters = parameters.ToImmutable(),
                callingConvention = actual.callingConvention
            });
        }
        ImmutableArray<InteropType> finalTypes = physical.types.Select(type => sourceRecordTypes.TryGetValue(type.id, out StableId originalType)
            ? type with { sourceNativeType = originalType } : type).ToImmutableArray();
        ImmutableArray<InteropFunction> finalFunctions = new CBridgeSemanticRemapper(plan.source, finalTypes, functions.ToImmutable(), semanticTypes).Apply();
        return new(libraryId, physical.targetId, physical.pointerSize, finalTypes, finalFunctions, physical.diagnostics)
        { byteOrder = physical.byteOrder };

        void VerifyNativeStorage(
            StableId sourceId,
            StableId actualId,
            bool compareTopQualifiers = true
        ) {
            NativeType source = Resolve(plan.source, sourceId), actual = Resolve(reparsed, actualId);
            if (compareTopQualifiers && HasConst(plan.source, sourceId) != HasConst(reparsed, actualId))
                throw new InvalidOperationException("A native C storage qualifier changed during bridge verification.");
            if (!checkedStorage.Add((source.id, actual.id))) return;
            NativeTypeFormatter formatter = new();
            if (formatter.Format(plan.source, source.id, preserveAliases: false) != formatter.Format(reparsed, actual.id, preserveAliases: false)
                || source.category != actual.category || source.size != actual.size || source.alignment != actual.alignment
                || source.primitiveKind != actual.primitiveKind || source.isSigned != actual.isSigned || source.arrayLength != actual.arrayLength
                || source.isRValueReference != actual.isRValueReference)
                throw new InvalidOperationException("A native C storage shape changed during bridge verification.");
            if (semanticTypes.TryGetValue(source.id, out StableId existingType) && existingType != actual.id)
                throw new InvalidOperationException("One source C storage type cannot map to multiple physical semantic identities.");
            semanticTypes[source.id] = actual.id;
            if (source.category == NativeTypeCategory.Record)
            {
                if (sourceRecordTypes.TryGetValue(actual.id, out StableId mapped) && mapped != source.id)
                    throw new InvalidOperationException("A reparsed C record cannot represent two distinct original storage identities.");
                sourceRecordTypes[actual.id] = source.id;
                if (source.declaration is not StableId sourceDeclaration || actual.declaration is not StableId actualDeclaration)
                    throw new InvalidOperationException("A native C record lost its declaration identity.");
                NativeDeclaration left = plan.source.GetDeclaration(sourceDeclaration), right = reparsed.GetDeclaration(actualDeclaration);
                NativeDeclaration[] expectedFields = left.children.Select(plan.source.GetDeclaration).Where(item => item.kind == "Field" && !item.isStatic).ToArray();
                NativeDeclaration[] actualFields = right.children.Select(reparsed.GetDeclaration).Where(item => item.kind == "Field" && !item.isStatic).ToArray();
                if (left.role != right.role || left.isCompleteDefinition != right.isCompleteDefinition || expectedFields.Length != actualFields.Length)
                    throw new InvalidOperationException("A native C record changed its complete field set.");
                for (int index = 0; index < expectedFields.Length; index++)
                {
                    NativeDeclaration expected = expectedFields[index], observed = actualFields[index];
                    if (expected.name != observed.name || expected.offset != observed.offset || expected.bitOffset != observed.bitOffset
                        || expected.bitWidth != observed.bitWidth || expected.storageSize != observed.storageSize
                        || expected.type is not StableId expectedType || observed.type is not StableId actualType)
                        throw new InvalidOperationException("A native C record field changed its exact position or storage.");
                    VerifyNativeStorage(expectedType, actualType);
                }
            }
            else if (source.category is NativeTypeCategory.Pointer or NativeTypeCategory.Reference or NativeTypeCategory.Array or NativeTypeCategory.Enumeration)
            {
                if (source.elementType is not StableId expectedElement || actual.elementType is not StableId actualElement)
                    throw new InvalidOperationException("A native C storage element lost its exact identity.");
                VerifyNativeStorage(expectedElement, actualElement);
            }
            else if (source.category == NativeTypeCategory.Function)
            {
                if (source.callingConvention != actual.callingConvention || source.hasPrototype != actual.hasPrototype
                    || source.isVariadic != actual.isVariadic || source.parameterTypes.Length != actual.parameterTypes.Length
                    || source.returnType is not StableId expectedResult || actual.returnType is not StableId actualResult)
                    throw new InvalidOperationException("A native C stored callable changed its exact prototype.");
                VerifyNativeStorage(expectedResult, actualResult);
                for (int index = 0; index < source.parameterTypes.Length; index++) VerifyNativeStorage(source.parameterTypes[index], actual.parameterTypes[index]);
            }
        }
        static bool HasConst(
            NativeModule module,
            StableId id
        ) {
            NativeType type = module.GetType(id);
            return type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true
                || type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is StableId element && HasConst(module, element);
        }
        void Check(
            StableId expected,
            StableId actual
        ) {
            BridgeType type = types[expected];
            NativeType parsed = Resolve(reparsed, actual);
            if (type.kind == BridgeTypeKind.NativeStorage)
            {
                // A carrier may deliberately add outer const to a by-value source record.
                // Its pointer owner checks that qualifier; nested native fields remain exact.
                VerifyNativeStorage(type.nativeType!.Value, actual, compareTopQualifiers: false);
            }
            else if (type.kind == BridgeTypeKind.Pointer)
            {
                if (parsed.category != NativeTypeCategory.Pointer || parsed.elementType is not StableId element
                    || (type.isConst || types[type.elementType!.Value] is { kind: BridgeTypeKind.NativeStorage, nativeType: StableId originalPointee }
                        && HasConst(plan.source, originalPointee)) != HasConst(reparsed, element))
                    throw new InvalidOperationException("A normalized C carrier lost its exact pointer indirection or pointee qualification.");
                Check(type.elementType!.Value, element);
            }
            else if (type.kind == BridgeTypeKind.Function)
            {
                if (parsed.category != NativeTypeCategory.Function || !parsed.hasPrototype || parsed.isVariadic
                    || parsed.callingConvention is not ("C" or "Default")
                    || parsed.parameterTypes.Length != type.parameterTypes.Length || parsed.returnType is null)
                    throw new InvalidOperationException("A normalized callback changed its complete physical prototype.");
                Check(type.returnType!.Value, parsed.returnType.Value);
                for (int index = 0; index < type.parameterTypes.Length; index++) Check(type.parameterTypes[index], parsed.parameterTypes[index]);
            }
            else if (type.kind == BridgeTypeKind.Handle)
            {
                if (parsed.category != NativeTypeCategory.Pointer || parsed.elementType is not StableId element
                    || Resolve(reparsed, element) is not { category: NativeTypeCategory.Record } record
                    || record.declaration is not StableId declaration || reparsed.GetDeclaration(declaration).name != type.cName)
                    throw new InvalidOperationException("A C callback context handle lost its exact opaque pointer identity.");
            }
            else if (type.kind == BridgeTypeKind.Enumeration)
            {
                Check(type.elementType!.Value, actual);
            }
            else if (type.kind == BridgeTypeKind.Scalar)
            {
                bool valid = type.cName switch
                {
                    "void" => parsed.primitiveKind == "Void",
                    "float" => parsed.primitiveKind == "Float" && parsed.size == 4,
                    "double" => parsed.primitiveKind == "Double" && parsed.size == 8,
                    "size_t" or "uintptr_t" => parsed.category == NativeTypeCategory.Primitive && parsed.isSigned == false && parsed.size == reparsed.pointerSize,
                    "ptrdiff_t" or "intptr_t" => parsed.category == NativeTypeCategory.Primitive && parsed.isSigned == true && parsed.size == reparsed.pointerSize,
                    _ => FixedInteger(type.cName, parsed)
                };
                if (!valid) throw new InvalidOperationException("A C scalar carrier changed its declared native width or category.");
            }
            else throw new NotSupportedException("The C bridge storage requires its explicit verifier implementation.");
        }
    }
    private static bool FixedInteger(
        string spelling,
        NativeType type
    ) {
        if (type.category != NativeTypeCategory.Primitive || type.isSigned is null) return false;
        string prefix = type.isSigned == true ? "int" : "uint";
        return type.size is 1 or 2 or 4 or 8 && spelling == prefix + (type.size.Value * 8).ToString(System.Globalization.CultureInfo.InvariantCulture) + "_t";
    }
    private static NativeType Resolve(
        NativeModule module,
        StableId id
    ) => StandardLibraryFactReader.Resolve(module, id);
}
