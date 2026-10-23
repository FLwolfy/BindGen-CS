using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Interop;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Inputs;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Compares a typed bridge plan with independently reparsed C declarations and restores its semantic facts.</summary>
public sealed class BridgeContractVerifier
{
    /// <summary>Rejects changed export shapes before associating lowering ownership and error contracts.</summary>
    /// <param name="plan">The immutable source-language operation plan.</param>
    /// <param name="reparsed">The actual generated C header facts.</param>
    /// <param name="libraryId">The logical library identity.</param>
    /// <returns>The exact reparsed C ABI enriched with declared lowering semantics.</returns>
    public InteropModule VerifyAndMerge(
        NativeBridgePlan plan,
        NativeModule reparsed,
        string libraryId
    ) {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(reparsed);
        if (plan.source.language == SourceLanguage.C)
            return new CPointerBridgeVerifier().VerifyAndMerge(plan, reparsed, libraryId);
        new CppBridgeOperationVerifier().Verify(plan);
        if (reparsed.language != SourceLanguage.C || plan.source.targetId != reparsed.targetId || plan.source.triple != reparsed.triple
            || plan.source.pointerSize != reparsed.pointerSize || plan.source.byteOrder != reparsed.byteOrder)
            throw new InvalidOperationException("The reparsed bridge has a different language or target ABI.");
        InteropModule abi = new CDeclarationAnalyzer().Analyze(reparsed, libraryId);
        if (abi.diagnostics.Any(diagnostic => diagnostic.isFailure))
            throw new InvalidOperationException(string.Join(Environment.NewLine, abi.diagnostics.Select(diagnostic => diagnostic.message)));
        Dictionary<string, InteropFunction> actual = abi.functions.ToDictionary(function => function.exportName, StringComparer.Ordinal);
        if (actual.Count != plan.exports.Length + 2 || plan.exports.Any(export => !actual.ContainsKey(export.name))
            || !actual.ContainsKey(plan.errorMessageExport) || !actual.ContainsKey(plan.errorReleaseExport))
            throw new InvalidOperationException("The reparsed C header changed the planned export set.");
        Dictionary<StableId, BridgeType> types = plan.types.ToDictionary(type => type.id);
        foreach (BridgeType callback in plan.types.Where(type => type.kind == BridgeTypeKind.Function))
        {
            NativeType[] aliases = reparsed.types.Where(type => type.category == NativeTypeCategory.Alias && type.declaration is StableId declaration
                && reparsed.GetDeclaration(declaration).name == callback.cName).ToArray();
            if (aliases.Length != 1 || !Matches(callback.id, aliases[0].id))
                throw new InvalidOperationException($"Bridge callback typedef '{callback.cName}' changed its exact planned native prototype.");
        }
        ImmutableArray<InteropFunction>.Builder merged = ImmutableArray.CreateBuilder<InteropFunction>();
        Evidence evidence = new(EvidenceLevel.Declared, "bridge-model:" + plan.modelRevision + ":" + plan.exportPrefix);
        StableId errorRelease = new("bridge-support:" + plan.exportPrefix + ":error-release");
        StableId allocator = new("bridge-allocator:" + libraryId);
        StableId errorAllocator = new("bridge-error-c-allocator:" + libraryId);
        BGCS.Analysis.Declarations.NativeSourceLocator originalLocations = new(plan.source);
        Dictionary<StableId, SourceLocation> sourceOrigins = [];
        foreach (BridgeExport export in plan.exports)
        {
            CppVirtualProxyPlan? generatedProxy = export.operation.virtualProxyId is { } proxyId
                ? plan.virtualProxies.Single(proxy => proxy.id == proxyId) : null;
            SourceLocation? callableSource = export.operation.declaration is StableId sourceCallable ? plan.source.GetDeclaration(sourceCallable).source
                : export.operation.ownerType is StableId ownerSource ? originalLocations.Locate(ownerSource)
                : generatedProxy is not null ? originalLocations.Locate(generatedProxy.nativeInterface) : null;
            string? defaultSignature = export.operation.kind == BridgeOperationKind.DefaultConstruct
                ? new BGCS.Analysis.Types.NativeTypeFormatter().DefaultConstructionSignature(plan.source, export.operation.ownerType!.Value) : null;
            ControlledSourceFragment[] explicitSources = export.parameters.Where(parameter => parameter.conversionExpression is not null).Select(parameter => parameter.conversionExpression!)
                .Concat(export.operation.invocationExpression is null ? [] : new[] { export.operation.invocationExpression })
                .Concat(export.resultExpression is null ? [] : new[] { export.resultExpression }).Concat(plan.sourceFragments).ToArray();
            explicitSources = explicitSources.Concat(export.operation.argumentGroups.Where(group => group.expression is not null).Select(group => group.expression!)).ToArray();
            Evidence exportEvidence = explicitSources.Any(fragment => fragment.evidence.level == EvidenceLevel.UserAsserted)
                ? new(EvidenceLevel.UserAsserted, "native-source-fragments:" + string.Join(",", explicitSources.Select(fragment => fragment.origin).Distinct(StringComparer.Ordinal))) : evidence;
            if (export.resultOwnership?.evidence.level == EvidenceLevel.UserAsserted) exportEvidence = export.resultOwnership.evidence;
            InteropFunction function = actual[export.name];
            CheckCallable(function);
            NativeType status = Resolve(function.returnType);
            if (status.category != NativeTypeCategory.Primitive || status.size != 4 || status.isSigned != true)
                throw new InvalidOperationException($"Bridge export '{export.name}' changed its fixed-width status result.");
            if (function.parameters.Length != export.parameters.Length + (export.resultType is null ? 1 : 2))
                throw new InvalidOperationException($"Bridge export '{export.name}' changed its parameter count.");
            ImmutableArray<InteropParameter>.Builder parameters = ImmutableArray.CreateBuilder<InteropParameter>();
            NativeDeclaration[] sourceParameters = export.operation.declaration is StableId callableId
                ? plan.source.GetDeclaration(callableId).children.Select(plan.source.GetDeclaration).Where(declaration => declaration.kind == "Parameter").ToArray() : [];
            int sourceParameterIndex = 0;
            for (int index = 0; index < export.parameters.Length; index++)
            {
                BridgeParameter expected = export.parameters[index];
                InteropParameter parameter = function.parameters[index];
                if (parameter.name != expected.name || !Matches(expected.type, parameter.type))
                    throw new InvalidOperationException($"Bridge export '{export.name}' changed parameter '{expected.name}'.");
                OwnershipContract? borrowed = export.operation.instanceParameter == expected.id && export.operation.kind != BridgeOperationKind.Destroy
                    ? new(NativeOwnershipKind.Borrowed, new(NativeLifetimeKind.Call, null, exportEvidence), exportEvidence) : null;
                BridgeArgumentBinding? groupedBinding = null;
                StandardSequenceArgument? sequence = null;
                BridgeArgumentGroup? argumentGroup = null;
                NativeDeclaration? original;
                if (!export.operation.argumentGroups.IsEmpty && expected.id != export.operation.instanceParameter)
                {
                    BridgeArgumentGroup group = export.operation.argumentGroups.Single(group => group.inputs.Any(input => input.parameter == expected.id));
                    argumentGroup = group;
                    sequence = group.standardSequence;
                    original = plan.source.GetDeclaration(group.sourceParameter);
                    groupedBinding = group.inputs.Single(input => input.parameter == expected.id);
                }
                else original = expected.id != export.operation.instanceParameter && sourceParameterIndex < sourceParameters.Length
                    ? sourceParameters[sourceParameterIndex++] : null;
                BufferContract? buffer = null;
                StringContract? text = null;
                if (sequence is not null && groupedBinding?.name == "data")
                {
                    NativeType pointer = Resolve(parameter.type);
                    if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId element)
                        throw new InvalidOperationException("A sequence adapter data carrier lost its actual reparsed pointer element.");
                    buffer = new(element, NativeLengthUnit.Elements,
                        types[expected.type].isConst ? NativeParameterDirection.Input : NativeParameterDirection.InputOutput,
                        new(NativeLifetimeKind.Call, null, sequence.evidence), sequence.evidence)
                    { retainsPointer = false, allowsNull = true, lengthParameter = argumentGroup!.inputs.Single(input => input.name == "length").parameter };
                    text = StandardSequenceTextFacts.Read(plan, argumentGroup);
                    if (text is not null) buffer = null;
                }
                Track(parameter.type, original?.source ?? callableSource);
                parameters.Add(parameter with
                {
                    id = expected.id,
                    ownership = borrowed,
                    buffer = buffer,
                    text = text,
                    sourceDeclaration = original?.id,
                    source = original?.source ?? callableSource,
                    sourceName = groupedBinding is null || argumentGroup is { expression: null, standardSequence: null }
                        ? original?.name : original is null ? expected.name : original.name + "_" + groupedBinding.name
                });
            }
            StableId? resultParameter = null;
            if (export.resultType is StableId result)
            {
                InteropParameter parameter = function.parameters[export.parameters.Length];
                NativeType storage = Resolve(parameter.type);
                if (parameter.name != "bgcs_result" || storage.category != NativeTypeCategory.Pointer || storage.elementType is not StableId element || !Matches(result, element))
                    throw new InvalidOperationException($"Bridge export '{export.name}' changed original-result storage.");
                resultParameter = new("bridge-result:" + export.id.value);
                OwnershipContract? ownership = export.resultRelease is StableId release ? new(NativeOwnershipKind.Owned,
                    new(NativeLifetimeKind.Owner, resultParameter, exportEvidence), exportEvidence)
                {
                    releaseFunction = release,
                    allocator = export.resultOwnership?.allocatorDomain ?? allocator,
                    releaseThreadDomain = export.resultOwnership?.releaseThreadDomain
                }
                    : export.resultBorrowsInstance || export.resultConversion is BridgeConversionKind.PointerCast or BridgeConversionKind.ReferenceAddress ? new(NativeOwnershipKind.Borrowed,
                        new(export.operation.kind is BridgeOperationKind.BaseCast or BridgeOperationKind.CheckedDerivedCast or BridgeOperationKind.VirtualProxyCast ? NativeLifetimeKind.Owner : NativeLifetimeKind.Unknown,
                            export.operation.kind is BridgeOperationKind.BaseCast or BridgeOperationKind.CheckedDerivedCast or BridgeOperationKind.VirtualProxyCast ? export.operation.instanceParameter : null, exportEvidence), exportEvidence) : null;
                CppVirtualProxyPlan? proxy = export.operation.kind == BridgeOperationKind.VirtualProxyConstruct ? generatedProxy : null;
                if (proxy?.releaseThreadDomain == "any" && ownership is not null)
                    ownership = ownership with { releaseThreadDomain = "any", evidence = proxy.cleanupEvidence };
                Track(parameter.type, callableSource);
                parameters.Add(parameter with { id = resultParameter.Value, ownership = ownership, source = callableSource });
            }
            InteropParameter error = function.parameters[^1];
            NativeType errorPointer = Resolve(error.type);
            if (error.name != "bgcs_error" || errorPointer.category != NativeTypeCategory.Pointer || !IsErrorPointer(errorPointer.elementType!.Value, false))
                throw new InvalidOperationException($"Bridge export '{export.name}' changed its owned error channel.");
            StableId errorId = new("bridge-error:" + export.id.value);
            OwnershipContract errorOwnership = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, errorId, evidence), evidence)
            { releaseFunction = errorRelease, allocator = errorAllocator, releaseThreadDomain = "any" };
            Track(function.returnType, callableSource);
            Track(error.type, callableSource);
            parameters.Add(error with { id = errorId, ownership = errorOwnership, source = callableSource });
            merged.Add(function with
            {
                id = export.id,
                parameters = parameters.ToImmutable(),
                documentation = export.documentation,
                documentationContent = export.operation.declaration is StableId documentedCallable
                    && export.documentation == plan.source.GetDeclaration(documentedCallable).documentation
                    ? plan.source.GetDeclaration(documentedCallable).documentationContent : null,
                inputAdapterOf = export.inputAdapterOf,
                sourceDeclaration = export.operation.declaration,
                source = callableSource,
                sourceQualifiedName = export.operation.finiteVariadic?.wrapper ?? (generatedProxy is not null ? generatedProxy.factory
                    : export.operation.declaration is StableId callable ? plan.source.GetDeclaration(callable).qualifiedName : defaultSignature?[..^2]),
                sourceSignature = export.operation.declaration is StableId signatureCallable
                    ? new BGCS.Analysis.Types.NativeTypeFormatter().CallableSignature(plan.source, plan.source.GetDeclaration(signatureCallable)) : defaultSignature,
                sourceName = export.operation.finiteVariadic is { } variant ? variant.wrapper.Split("::", StringSplitOptions.None)[^1]
                    : generatedProxy is not null ? generatedProxy.factory.Split("::", StringSplitOptions.None)[^1]
                    : export.operation.declaration is StableId namedCallable ? plan.source.GetDeclaration(namedCallable).name
                    : export.operation.standardValue is { } standard ? standard.action.ToString()
                    : export.operation.castTarget is StableId castTarget ? plan.source.GetDeclaration(StandardLibraryFactReader.Resolve(plan.source, castTarget).declaration!.Value).name : null,
                sourceRole = export.operation.kind == BridgeOperationKind.VirtualProxyDestroy ? "Destroy"
                    : export.operation.kind == BridgeOperationKind.VirtualProxyCast ? "BaseCast"
                    : export.operation.declaration is StableId roleCallable ? plan.source.GetDeclaration(roleCallable).role
                    : export.operation.kind.ToString(),
                sourceOwnerType = export.operation.ownerType,
                sourceGeneratedOwnerType = generatedProxy?.generatedType,
                error = new("0", exportEvidence) { valueParameter = resultParameter, errorParameter = errorId, errorOwnership = errorOwnership }
            });
        }
        InteropFunction releaseError = actual[plan.errorReleaseExport];
        CheckCallable(releaseError);
        if (Resolve(releaseError.returnType).primitiveKind != "Void" || releaseError.parameters.Length != 1 || !IsErrorPointer(releaseError.parameters[0].type, false))
            throw new InvalidOperationException("The bridge error-release protocol changed.");
        merged.Add(releaseError with { id = errorRelease });
        InteropFunction message = actual[plan.errorMessageExport];
        CheckCallable(message);
        NativeType textPointer = Resolve(message.returnType);
        if (message.parameters.Length != 1 || !IsErrorPointer(message.parameters[0].type, true)
            || textPointer.category != NativeTypeCategory.Pointer || !HasConst(textPointer.elementType!.Value) || Resolve(textPointer.elementType.Value).size != 1)
            throw new InvalidOperationException("The bridge borrowed error-message protocol changed.");
        merged.Add(message with
        {
            id = new("bridge-support:" + plan.exportPrefix + ":error-message"),
            returnOwnership = new(NativeOwnershipKind.Borrowed, new(NativeLifetimeKind.Owner, message.parameters[0].id, evidence), evidence)
        });
        Dictionary<string, BridgeType> handles = plan.types.Where(type => type.kind is BridgeTypeKind.Handle or BridgeTypeKind.Enumeration or BridgeTypeKind.Record).ToDictionary(type => type.cName, StringComparer.Ordinal);
        var dtoOrigins = plan.types.Where(type => type.kind == BridgeTypeKind.Record).Select(type => type.nativeType!.Value).ToHashSet();
        ImmutableArray<InteropType> enriched = abi.types.Select(type =>
        {
            if (sourceOrigins.TryGetValue(type.id, out SourceLocation typeSource)) type = type with { source = typeSource };
            if (type.declarationName is null || !handles.TryGetValue(type.declarationName, out BridgeType? handle)) return type;
            if (handle.generatedType is StableId generated)
            {
                CppVirtualProxyPlan proxy = plan.virtualProxies.Single(candidate => candidate.generatedType == generated);
                return type with
                {
                    sourceGeneratedType = generated,
                    sourceNativeType = null,
                    source = originalLocations.Locate(proxy.nativeInterface),
                    sourceQualifiedName = proxy.proxyType
                };
            }
            NativeType source = plan.source.GetType(handle.nativeType!.Value);
            NativeDeclaration original = plan.source.GetDeclaration(source.declaration!.Value);
            if (handle.kind == BridgeTypeKind.Enumeration)
            {
                if (type.category != NativeTypeCategory.Alias || type.elementType is null) throw new InvalidOperationException("A named bridge enumeration lost its fixed-width C typedef.");
                return type with
                {
                    category = NativeTypeCategory.Enumeration,
                    source = original.source,
                    sourceNativeType = source.id,
                    sourceQualifiedName = original.qualifiedName,
                    documentation = original.documentation,
                    enumValues = original.children.Select(plan.source.GetDeclaration).Where(child => child.kind == "EnumItem")
                        .Select(child => new InteropEnumValue(child.id, child.name, child.value, child.evaluatedIntegralValue)
                        { documentation = child.documentation }).ToImmutableArray()
                };
            }
            if (handle.kind == BridgeTypeKind.Record)
            {
                if (type.category != NativeTypeCategory.Record) return type;
                if (!Matches(handle.id, type.id)) throw new InvalidOperationException("A DTO lost its independently reparsed field layout.");
                return type with
                {
                    source = original.source,
                    sourceNativeType = source.id,
                    sourceQualifiedName = original.qualifiedName,
                    recordKind = "CppDto",
                    documentation = original.documentation,
                    fields = handle.recordStorage!.fields.Select(field => type.fields.Single(actualField => actualField.name == field.name) with
                    { id = field.declaration, documentation = plan.source.GetDeclaration(field.declaration).documentation }).ToImmutableArray()
                };
            }
            return type with
            {
                source = original.source,
                sourceNativeType = source.id,
                documentation = original.documentation,
                sourceQualifiedName = original.qualifiedName
                + (dtoOrigins.Contains(source.id) ? "Native" : string.Empty)
            };
        }).ToImmutableArray();
        InteropModule mergedAbi = new(libraryId, abi.targetId, abi.pointerSize, enriched, merged.ToImmutable(), abi.diagnostics)
        { byteOrder = abi.byteOrder, constants = abi.constants, functionAliases = abi.functionAliases, stringConstants = abi.stringConstants };
        return new NativeValueProtocolBuilder().Attach(plan, new VirtualProxyProtocolBuilder().Attach(plan, mergedAbi));

        void Track(
            StableId id,
            SourceLocation? location
        ) {
            if (location is not SourceLocation source || string.IsNullOrWhiteSpace(source.file) || source.line <= 0 || source.column <= 0 || !sourceOrigins.TryAdd(id, source)) return;
            NativeType type = reparsed.GetType(id);
            foreach (StableId child in new[] { type.elementType, type.canonicalType, type.returnType }.Where(child => child is not null).Select(child => child!.Value)) Track(child, source);
            foreach (StableId parameter in type.parameterTypes) Track(parameter, source);
        }

        NativeType Resolve(StableId id)
        {
            HashSet<StableId> visited = [];
            NativeType type = reparsed.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visited.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("The reparsed native alias graph is incomplete.");
                type = reparsed.GetType(element);
            }
            return type;
        }

        bool HasConst(StableId id)
        {
            NativeType type = reparsed.GetType(id);
            return type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true
                || type.category is NativeTypeCategory.Qualified or NativeTypeCategory.Alias && type.elementType is StableId element && HasConst(element);
        }

        bool Matches(
            StableId expectedId,
            StableId actualId
        ) {
            BridgeType expected = types[expectedId];
            NativeType native = Resolve(actualId);
            if (expected.kind == BridgeTypeKind.Pointer)
                return native.category == NativeTypeCategory.Pointer && native.elementType is StableId element && HasConst(element) == expected.isConst && Matches(expected.elementType!.Value, element);
            if (expected.kind == BridgeTypeKind.Handle)
                return native.category == NativeTypeCategory.Record && native.declaration is StableId declaration && reparsed.GetDeclaration(declaration).name == expected.cName && native.size is null;
            if (expected.kind == BridgeTypeKind.Function)
                return native.category == NativeTypeCategory.Function && native.returnType is StableId functionResult && expected.returnType is StableId plannedResult
                    && native.hasPrototype == expected.hasPrototype && native.isVariadic == expected.isVariadic
                    && (native.callingConvention == expected.callingConvention || native.callingConvention is "C" or "Default" && expected.callingConvention is "C" or "Default")
                    && native.parameterTypes.Length == expected.parameterTypes.Length && Matches(plannedResult, functionResult)
                    && expected.parameterTypes.Zip(native.parameterTypes).All(parameter => Matches(parameter.First, parameter.Second));
            if (expected.kind == BridgeTypeKind.FixedArray)
                return native.category == NativeTypeCategory.Array && native.arrayLength == expected.arrayLength
                    && native.elementType is StableId arrayElementId && Matches(expected.elementType!.Value, arrayElementId);
            if (expected.kind == BridgeTypeKind.Record)
            {
                if (native.category != NativeTypeCategory.Record || native.declaration is not StableId dtoDeclarationId
                    || reparsed.GetDeclaration(dtoDeclarationId).name != expected.cName || expected.recordStorage is not BridgeRecord storage
                    || native.size != storage.size || native.alignment != storage.alignment) return false;
                var fields = reparsed.GetDeclaration(dtoDeclarationId).children.Select(reparsed.GetDeclaration).Where(field => field.kind == "Field").ToArray();
                foreach (BridgeRecordField field in storage.fields)
                {
                    NativeDeclaration[] actualFields = fields.Where(actualField => actualField.name == field.name).ToArray();
                    if (actualFields.Length != 1 || actualFields[0].offset != field.offset || actualFields[0].bitWidth is not null
                        || actualFields[0].type is not StableId fieldType || !Matches(field.type, fieldType)) return false;
                }
                long end = 0;
                List<(long Offset, long Size)> padding = [];
                foreach (BridgeRecordField field in storage.fields)
                {
                    if (field.offset > end) padding.Add((end, field.offset - end));
                    end = checked(field.offset + StandardLibraryFactReader.Resolve(plan.source, plan.source.GetDeclaration(field.declaration).type!.Value).size!.Value);
                }
                if (end < storage.size) padding.Add((end, storage.size - end));
                var extra = fields.Where(field => !storage.fields.Any(plannedField => plannedField.name == field.name)).ToArray();
                if (extra.Length != padding.Count) return false;
                foreach (var gap in padding)
                {
                    NativeDeclaration[] pad = extra.Where(field => field.offset == gap.Offset).ToArray();
                    if (pad.Length != 1 || pad[0].type is not StableId padType) return false;
                    NativeType bytes = Resolve(padType);
                    if (bytes.category != NativeTypeCategory.Array || bytes.arrayLength != gap.Size || bytes.elementType is not StableId byteType
                        || Resolve(byteType) is not { category: NativeTypeCategory.Primitive, size: 1, isSigned: false }) return false;
                }
                return true;
            }
            if (expected.kind == BridgeTypeKind.Enumeration)
            {
                NativeType alias = reparsed.GetType(actualId);
                while (alias.category == NativeTypeCategory.Qualified) alias = reparsed.GetType(alias.elementType!.Value);
                return alias.category == NativeTypeCategory.Alias && alias.declaration is StableId declaration && reparsed.GetDeclaration(declaration).name == expected.cName
                    && Matches(expected.elementType!.Value, alias.elementType!.Value);
            }
            if (expected.cName == "void") return native.primitiveKind == "Void";
            if (expected.cName == "float") return native.primitiveKind == "Float" && native.size == 4;
            if (expected.cName == "double") return native.primitiveKind == "Double" && native.size == 8;
            bool signed = expected.cName.StartsWith("int", StringComparison.Ordinal);
            string digits = expected.cName[(signed ? 3 : 4)..^2];
            return native.category == NativeTypeCategory.Primitive && native.isSigned == signed && long.TryParse(digits, out long width) && native.size * 8 == width;
        }

        bool IsErrorPointer(
            StableId id,
            bool isConst
        ) {
            NativeType pointer = Resolve(id);
            if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId element || HasConst(element) != isConst) return false;
            NativeType errorType = Resolve(element);
            return errorType.category == NativeTypeCategory.Record && errorType.declaration is StableId declaration && reparsed.GetDeclaration(declaration).name == plan.errorTypeName && errorType.size is null;
        }

        static void CheckCallable(InteropFunction function)
        {
            if (!function.hasPrototype || function.isVariadic || function.callingConvention != "C")
                throw new InvalidOperationException($"Bridge export '{function.exportName}' changed its fixed C calling convention.");
        }
    }
}
