using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Checks typed native actions and ownership pairs independently of the C++ emitter.</summary>
public sealed class CppBridgeOperationVerifier
{
    /// <summary>Rejects incomplete, mismatched or unsafe operation plans before candidate source is written.</summary>
    /// <param name="module">The frozen bridge and its originating native graph.</param>
    public void Verify(CppBridgeModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _ = CppFamilyBindingValidator.Freeze(module.familyBindings);
        new StandardLibraryFactReader(module.familyBindings).ValidateBindings(module.source);
        Dictionary<StableId, BridgeType> types = module.types.ToDictionary(type => type.id);
        Dictionary<StableId, BridgeExport> exports = module.exports.ToDictionary(export => export.id);
        new BridgeRecordVerifier().Verify(module);
        foreach (BridgeType function in module.types.Where(type => type.kind == BridgeTypeKind.Function))
        {
            if (function.nativeType is not StableId nativeId || function.returnType is not StableId result
                || !types.ContainsKey(result) || function.parameterTypes.IsDefault || function.parameterTypes.Any(parameter => !types.ContainsKey(parameter))
                || !function.hasPrototype || function.callingConvention is not ("C" or "Default")
                || function.isVariadic && function.parameterTypes.IsEmpty)
                throw new InvalidOperationException("A C callback representation needs a complete supported function signature and registered carriers.");
            NativeType native = Resolve(nativeId);
            if (native.category != NativeTypeCategory.Function || native.returnType is not StableId nativeResult
                || !native.hasPrototype || native.isVariadic != function.isVariadic || native.callingConvention != function.callingConvention
                || native.parameterTypes.Length != function.parameterTypes.Length || !CallbackValue(result, nativeResult, true, []))
                throw new InvalidOperationException("A C callback representation changed its original callable ABI.");
            for (int parameter = 0; parameter < function.parameterTypes.Length; parameter++)
                if (!CallbackValue(function.parameterTypes[parameter], native.parameterTypes[parameter], false, []))
                    throw new InvalidOperationException("A native callback argument needs a conversion thunk rather than a function-pointer cast.");
        }
        new VirtualProxyProtocolBuilder().VerifyBridge(module);
        ControlledSourceFragmentVerifier fragments = new();
        foreach (ControlledSourceFragment fragment in module.sourceFragments)
        {
            if (fragment.kind != SourceFragmentKind.TranslationUnit) throw new InvalidOperationException("A native helper fragment has an incorrect source placement.");
            fragments.Validate(fragment, module.source, module.headers, []);
        }

        bool CallbackValue(
            StableId carrierId,
            StableId nativeId,
            bool allowVoid,
            HashSet<(StableId, StableId)> active
        ) {
            if (!active.Add((carrierId, nativeId))) return false;
            try
            {
                BridgeType carrier = types[carrierId];
                NativeType native = Resolve(nativeId);
                if (carrier.kind == BridgeTypeKind.Pointer)
                    return native.category == NativeTypeCategory.Pointer && native.elementType is StableId pointee && carrier.elementType is StableId carrierElement
                        && CallbackPointee(carrierElement, pointee, active);
                if (carrier.kind == BridgeTypeKind.Enumeration)
                    return native.category == NativeTypeCategory.Enumeration && native.elementType is StableId underlying && carrier.elementType is StableId carrierUnderlying
                        && CallbackValue(carrierUnderlying, underlying, false, active);
                if (carrier.kind != BridgeTypeKind.Scalar || native.category != NativeTypeCategory.Primitive || native.primitiveKind == "Bool") return false;
                if (native.primitiveKind == "Void") return allowVoid && carrier.cName == "void";
                if (native.primitiveKind == "Float") return carrier.cName == "float" && native.size == 4;
                if (native.primitiveKind == "Double") return carrier.cName == "double" && native.size == 8;
                return native.size is 1 or 2 or 4 or 8 && native.isSigned is bool signed
                    && carrier.cName == (signed ? "int" : "uint") + (native.size.Value * 8).ToString(System.Globalization.CultureInfo.InvariantCulture) + "_t";
            }
            finally { active.Remove((carrierId, nativeId)); }
        }

        bool CallbackPointee(
            StableId carrierId,
            StableId nativeId,
            HashSet<(StableId, StableId)> active
        ) {
            BridgeType carrier = types[carrierId];
            NativeType native = Resolve(nativeId);
            if (carrier.kind == BridgeTypeKind.Handle) return native.category == NativeTypeCategory.Record && carrier.nativeType == native.id;
            if (carrier.kind == BridgeTypeKind.Function) return native.category == NativeTypeCategory.Function && carrier.nativeType == native.id;
            return CallbackValue(carrierId, nativeId, true, active);
        }
        foreach (BridgeExport export in module.exports)
        {
            if (export.operation is null || !Enum.IsDefined(export.operation.kind) || !Enum.IsDefined(export.resultConversion))
                Fail(export, "The action or result conversion is unknown.");
            if (export.operation.argumentGroups.IsDefault) Fail(export, "Native argument groups must be initialized immutable arrays.");
            if (export.inputAdapterOf is StableId primaryId)
            {
                if (!exports.TryGetValue(primaryId, out BridgeExport? primary) || primary.inputAdapterOf is not null
                    || primary.id == export.id || primary.operation.kind != export.operation.kind
                    || primary.operation.declaration != export.operation.declaration || primary.operation.ownerType != export.operation.ownerType
                    || primary.operation.instanceParameter != export.operation.instanceParameter
                    || primary.resultType != export.resultType || primary.nativeResultType != export.nativeResultType
                    || primary.resultConversion != export.resultConversion || primary.resultRelease != export.resultRelease
                    || primary.resultExpression != export.resultExpression || primary.resultOwnership != export.resultOwnership
                    || export.operation.invocationExpression is not null || export.resultExpression is not null || export.resultOwnership is not null
                    || export.operation.kind is not (BridgeOperationKind.FreeCall or BridgeOperationKind.StaticCall or BridgeOperationKind.InstanceCall)
                    || !export.operation.argumentGroups.Any(group => group.standardSequence is not null))
                    Fail(export, "An input adapter changed its preserved primary operation or result contract.");
                if (!SequenceAdapterResultFacts.IsNonEscaping(module, export))
                    Fail(export, "A sequence adapter result could escape its temporary argument storage.");
            }
            if (export.operation!.kind == BridgeOperationKind.StandardValue)
            {
                if (export.operation.argumentGroups.IsDefault || !export.operation.argumentGroups.IsEmpty || export.resultOwnership is not null)
                    Fail(export, "A finite standard-library action cannot carry unrelated native argument groups.");
                new StandardValueOperationVerifier().Verify(module, export);
                continue;
            }
            if (export.operation.standardValue is not null || export.resultBorrowsInstance)
                Fail(export, "An ordinary native action carries an unrelated standard-library protocol.");
            if (export.operation.invocationExpression is ControlledSourceFragment invocation)
            {
                if (invocation.kind != SourceFragmentKind.Expression || invocation.text.Split("{invocation}", StringSplitOptions.None).Length != 2)
                    Fail(export, "An invocation expression must wrap exactly one independently planned native call.");
                fragments.Validate(invocation, module.source, module.headers, ["invocation"]);
            }
            BridgeOperation operation = export.operation!;
            NativeDeclaration? callable = operation.declaration is StableId declarationId ? module.source.GetDeclaration(declarationId) : null;
            NativeType? owner = operation.ownerType is StableId ownerId ? Resolve(ownerId) : null;
            if (owner is not null && (owner.category != NativeTypeCategory.Record || owner.declaration is null))
                Fail(export, "The action owner is not a declared native record.");
            NativeDeclaration? ownerDeclaration = owner?.declaration is StableId ownerDeclarationId ? module.source.GetDeclaration(ownerDeclarationId) : null;
            bool needsInstance = operation.kind is BridgeOperationKind.InstanceCall or BridgeOperationKind.Destroy or BridgeOperationKind.BaseCast or BridgeOperationKind.CheckedDerivedCast;
            if (needsInstance != (operation.instanceParameter is not null))
                Fail(export, "The action has a missing or unexpected instance parameter.");
            if ((operation.kind != BridgeOperationKind.FreeCall) != (owner is not null))
                Fail(export, "The action has a missing or unexpected owner type.");
            if (callable is not null && (callable.kind != "Function" || callable.isDeleted || callable.visibility is "Private" or "Protected"))
                Fail(export, "The action is not an accessible native callable.");
            if (callable is not null && (callable.isVariadic || !callable.hasPrototype || callable.templateParameters.Length != 0))
                Fail(export, "The action needs a concrete fixed native prototype.");
            if (operation.kind is BridgeOperationKind.FreeCall or BridgeOperationKind.StaticCall or BridgeOperationKind.InstanceCall or BridgeOperationKind.Construct && callable is null)
                Fail(export, "The action omitted its native callable identity.");
            if (ownerDeclaration is not null && callable is not null && callable.parent != ownerDeclaration.id)
                Fail(export, "The callable belongs to a different native record.");
            if (operation.kind == BridgeOperationKind.FreeCall && callable?.parent is StableId parent && module.source.GetDeclaration(parent).kind == "Class")
                Fail(export, "A record member cannot be called as a free function.");
            if (operation.kind == BridgeOperationKind.StaticCall && callable?.isStatic != true)
                Fail(export, "A static action requires a static native member.");
            if (operation.kind == BridgeOperationKind.InstanceCall && (callable?.isStatic != false || callable.role is "Constructor" or "Destructor"))
                Fail(export, "An instance action requires an ordinary nonstatic member.");
            if (operation.kind == BridgeOperationKind.Construct && (callable?.role != "Constructor" || ownerDeclaration?.isAbstract == true))
                Fail(export, "Construction requires a concrete accessible native constructor.");
            if (operation.kind == BridgeOperationKind.DefaultConstruct && (callable is not null || ownerDeclaration is null
                || ownerDeclaration.isDefaultConstructible != true || ownerDeclaration.isAbstract || !ownerDeclaration.isCompleteDefinition
                || ownerDeclaration.isSystemDeclaration || ownerDeclaration.templateParameters.Length != 0
                || ownerDeclaration.children.Select(module.source.GetDeclaration).Any(declaration => declaration.role == "Constructor")
                || !Accessible(ownerDeclaration) || export.parameters.Length != 0
                || export.resultConversion != BridgeConversionKind.OwnedValue || export.operation.invocationExpression is not null))
                Fail(export, "Default construction requires compiler-established accessible complete concrete owner facts without a declared constructor or source invocation.");
            if (operation.kind == BridgeOperationKind.Destroy && callable is not null && callable.role != "Destructor")
                Fail(export, "A release action names a non-destructor callable.");
            if (operation.kind is not (BridgeOperationKind.BaseCast or BridgeOperationKind.CheckedDerivedCast) && operation.castTarget is not null)
                Fail(export, "Only a base conversion may carry a cast destination.");
            if (operation.kind == BridgeOperationKind.BaseCast)
            {
                if (callable is not null || operation.castTarget is not StableId destination
                    || ownerDeclaration is null || !ownerDeclaration.baseClasses.Any(item => item.visibility == "Public" && Resolve(item.type).id == Resolve(destination).id))
                    Fail(export, "A base conversion requires a declared public native base.");
                if (export.parameters.Length != 1 || export.resultConversion != BridgeConversionKind.PointerCast || export.resultRelease is not null)
                    Fail(export, "A base conversion must return one borrowed pointer from one instance.");
            }
            if (operation.kind == BridgeOperationKind.CheckedDerivedCast)
            {
                NativeType? derived = operation.castTarget is StableId destination ? Resolve(destination) : null;
                NativeDeclaration? derivedDeclaration = derived?.declaration is StableId declaration ? module.source.GetDeclaration(declaration) : null;
                if (callable is not null || derived?.category != NativeTypeCategory.Record || derivedDeclaration is null || ownerDeclaration is null
                    || !derivedDeclaration.baseClasses.Any(item => item.visibility == "Public" && Resolve(item.type).id == owner!.id)
                    || !IsPolymorphic(ownerDeclaration, []))
                    Fail(export, "A checked derived conversion requires a public native base and a polymorphic source.");
                if (export.parameters.Length != 1 || export.resultConversion != BridgeConversionKind.PointerCast || export.resultRelease is not null)
                    Fail(export, "A checked derived conversion must return one borrowed pointer from one instance.");
            }
            BridgeParameter? instance = export.parameters.SingleOrDefault(parameter => parameter.id == operation.instanceParameter);
            if (needsInstance && (instance is null || instance.conversion != BridgeConversionKind.PointerCast || Resolve(instance.nativeType).id != owner!.id))
                Fail(export, "The instance representation belongs to another native type.");
            if (instance is not null)
            {
                BridgeType instanceType = types[instance.type];
                if (!IsHandlePointer(instanceType, owner!.id) || operation.kind != BridgeOperationKind.InstanceCall && instanceType.isConst
                    || operation.kind == BridgeOperationKind.InstanceCall && callable?.isConst != instanceType.isConst)
                    Fail(export, "The opaque instance or its const qualification is incorrect.");
            }
            BridgeParameter[] arguments = export.parameters.Where(parameter => parameter.id != operation.instanceParameter).ToArray();
            NativeDeclaration[] nativeArguments = callable?.children.Select(module.source.GetDeclaration).Where(child => child.kind == "Parameter").ToArray() ?? [];
            if (operation.argumentGroups.IsDefault) Fail(export, "Native argument groups must be initialized immutable arrays.");
            Dictionary<StableId, NativeDeclaration> origins = [];
            HashSet<StableId> expanded = [];
            if (operation.argumentGroups.IsEmpty)
            {
                if (arguments.Length != nativeArguments.Length) Fail(export, "The action changed native argument arity.");
                for (int index = 0; index < arguments.Length; index++) origins.Add(arguments[index].id, nativeArguments[index]);
            }
            else
            {
                if (operation.argumentGroups.Length != nativeArguments.Length || callable is null)
                    Fail(export, "Native argument groups changed the original callable arity.");
                Dictionary<StableId, BridgeParameter> available = arguments.ToDictionary(parameter => parameter.id);
                for (int index = 0; index < operation.argumentGroups.Length; index++)
                {
                    BridgeArgumentGroup group = operation.argumentGroups[index];
                    if (group is null) Fail(export, "A native argument group cannot be null.");
                    NativeDeclaration original = nativeArguments[index];
                    if (group.sourceParameter != original.id || group.nativeType != original.type || group.inputs.IsDefaultOrEmpty)
                        Fail(export, "A native argument group lost its original parameter identity or carrier inputs.");
                    HashSet<string> names = new(StringComparer.Ordinal);
                    foreach (BridgeArgumentBinding binding in group.inputs)
                    {
                        string? bindingName = binding?.name;
                        if (binding is null || string.IsNullOrWhiteSpace(bindingName)) Fail(export, "A native argument binding must have a nonempty placeholder identity.");
                        if (!System.Text.RegularExpressions.Regex.IsMatch(bindingName, "^[A-Za-z][A-Za-z0-9]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
                            || bindingName == "nativeType" || !names.Add(bindingName) || !available.ContainsKey(binding.parameter) || !origins.TryAdd(binding.parameter, original))
                            Fail(export, "Native argument groups have duplicate, missing or undeclared C carrier coverage.");
                        if (group.expression is not null || group.standardSequence is not null) expanded.Add(binding.parameter);
                    }
                    if (group.standardSequence is not null)
                        new StandardSequenceArgumentVerifier().Verify(module, export, group);
                    else if (group.expression is ControlledSourceFragment expression)
                    {
                        if (expression.kind != SourceFragmentKind.Expression) Fail(export, "A native argument group requires an expression source fragment.");
                        fragments.Validate(expression, module.source, module.headers, names.Append("nativeType"));
                    }
                    else if (group.inputs.Length != 1) Fail(export, "Multiple native carriers require one explicit argument conversion expression.");
                }
                if (origins.Count != arguments.Length) Fail(export, "Native argument groups omitted an exported C carrier.");
            }
            for (int index = 0; index < arguments.Length; index++)
            {
                BridgeParameter parameter = arguments[index];
                if (parameter.nativeType != origins[parameter.id].type)
                    Fail(export, "A parameter lost its corresponding native type identity.");
                BridgeType carrier = types[parameter.type];
                NativeType native = Resolve(parameter.nativeType);
                if (carrier.kind == BridgeTypeKind.Handle || carrier.kind == BridgeTypeKind.Scalar && carrier.cName == "void")
                    Fail(export, "An opaque or void type cannot be passed as C value storage.");
                if (expanded.Contains(parameter.id))
                {
                    if (parameter.conversionExpression is not null || parameter.managedProjection is not null
                        || parameter.conversion != (carrier.kind == BridgeTypeKind.Pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast))
                        Fail(export, "A grouped C carrier cannot declare an independent conversion or single-value managed lifetime projection.");
                    continue;
                }
                switch (parameter.conversion)
                {
                    case BridgeConversionKind.SourceExpression when carrier.kind is BridgeTypeKind.Scalar or BridgeTypeKind.Enumeration or BridgeTypeKind.Pointer:
                        if (parameter.conversionExpression is not ControlledSourceFragment expression || expression.kind != SourceFragmentKind.Expression)
                            Fail(export, "An explicit parameter conversion omitted its declared native expression.");
                        fragments.Validate(parameter.conversionExpression!, module.source, module.headers, ["value", "nativeType"]);
                        break;
                    case BridgeConversionKind.ScalarCast when carrier.kind is BridgeTypeKind.Scalar or BridgeTypeKind.Enumeration:
                        break;
                    case BridgeConversionKind.PointerCast when carrier.kind == BridgeTypeKind.Pointer && native.category == NativeTypeCategory.Pointer:
                        break;
                    case BridgeConversionKind.Dereference when carrier.kind == BridgeTypeKind.Pointer && native.category is NativeTypeCategory.Reference or NativeTypeCategory.Record:
                        break;
                    case BridgeConversionKind.RecordCopy when carrier.kind == BridgeTypeKind.Pointer && carrier.isConst
                        && native.category == NativeTypeCategory.Record && carrier.elementType is StableId recordElement
                        && types[recordElement].kind == BridgeTypeKind.Record && types[recordElement].nativeType == native.id:
                        break;
                    case BridgeConversionKind.Move when carrier.kind == BridgeTypeKind.Pointer && native.category == NativeTypeCategory.Reference && native.isRValueReference:
                        break;
                    case BridgeConversionKind.Move when carrier.kind == BridgeTypeKind.Pointer && native.category == NativeTypeCategory.Record
                        && new StandardLibraryFactReader(module.familyBindings).Read(module.source, native.id)?.family == StandardValueKind.UniquePointer && IsHandlePointer(carrier, native.id) && !carrier.isConst:
                        break;
                    default:
                        Fail(export, "An argument conversion does not match its C carrier and native category.");
                        break;
                }
                if (parameter.conversion != BridgeConversionKind.SourceExpression && parameter.conversionExpression is not null)
                    Fail(export, "An ordinary input conversion carries an unrelated source expression.");
            }
            if (export.resultType is not StableId resultId)
            {
                if (export.nativeResultType is not null || export.resultRelease is not null || export.resultOwnership is not null || operation.kind is BridgeOperationKind.Construct or BridgeOperationKind.DefaultConstruct or BridgeOperationKind.BaseCast or BridgeOperationKind.CheckedDerivedCast
                    || callable?.returnType is StableId nativeResult && Resolve(nativeResult).primitiveKind != "Void")
                    Fail(export, "The action discarded its native result or retained an unrelated release.");
                continue;
            }
            BridgeType result = types[resultId];
            if (export.nativeResultType is not StableId resultNativeId || result.kind == BridgeTypeKind.Handle || result.kind == BridgeTypeKind.Scalar && result.cName == "void")
                Fail(export, "A returned value has no usable native and C representation.");
            NativeType resultNative = Resolve(export.nativeResultType!.Value);
            if (export.resultOwnership is BridgeResultOwnership ownership)
            {
                if (result.kind != BridgeTypeKind.Pointer || result.isConst || ownership.releaseExport != export.resultRelease
                    || string.IsNullOrWhiteSpace(ownership.allocatorDomain.value) || ownership.releaseThreadDomain is not (null or "any" or "unknown")
                    || ownership.evidence is null || ownership.evidence.level is not (EvidenceLevel.Declared or EvidenceLevel.UserAsserted)
                    || !exports.TryGetValue(ownership.releaseExport, out BridgeExport? cleanup) || cleanup.id == export.id
                    || cleanup.parameters.Length != 1 || cleanup.parameters[0].type != result.id || cleanup.resultType is not null
                    || cleanup.operation.kind is not (BridgeOperationKind.FreeCall or BridgeOperationKind.StaticCall or BridgeOperationKind.Destroy))
                    Fail(export, "An owned native conversion requires its exact compatible typed release export, allocator domain and explicit assertion evidence.");
            }
            StableId? expectedResult = operation.kind is BridgeOperationKind.Construct or BridgeOperationKind.DefaultConstruct ? owner?.id
                : operation.kind is BridgeOperationKind.BaseCast or BridgeOperationKind.CheckedDerivedCast ? operation.castTarget : callable?.returnType;
            if (expectedResult is null || Resolve(expectedResult.Value).id != resultNative.id)
                Fail(export, "The result changed its native callable identity.");
            switch (export.resultConversion)
            {
                case BridgeConversionKind.SourceExpression when result.kind is BridgeTypeKind.Scalar or BridgeTypeKind.Enumeration or BridgeTypeKind.Pointer:
                    if (export.resultExpression is not ControlledSourceFragment expression || expression.kind != SourceFragmentKind.Expression
                        || export.resultRelease is not null && export.resultOwnership is null)
                        Fail(export, "An explicit result conversion needs its declared expression and an exact typed ownership contract for cleanup.");
                    fragments.Validate(export.resultExpression!, module.source, module.headers, ["value", "resultType"]);
                    break;
                case BridgeConversionKind.ScalarCast when result.kind is BridgeTypeKind.Scalar or BridgeTypeKind.Enumeration:
                    break;
                case BridgeConversionKind.RecordCopy when result.kind == BridgeTypeKind.Record && result.nativeType == resultNative.id
                    && resultNative.category == NativeTypeCategory.Record && export.resultRelease is null && export.resultOwnership is null:
                    break;
                case BridgeConversionKind.PointerCast when result.kind == BridgeTypeKind.Pointer && (resultNative.category == NativeTypeCategory.Pointer || operation.kind is BridgeOperationKind.BaseCast or BridgeOperationKind.CheckedDerivedCast):
                    if (operation.kind is BridgeOperationKind.BaseCast or BridgeOperationKind.CheckedDerivedCast && (!IsHandlePointer(result, resultNative.id) || result.isConst))
                        Fail(export, "A native inheritance conversion changed its exact nonconst destination object representation.");
                    break;
                case BridgeConversionKind.ReferenceAddress when result.kind == BridgeTypeKind.Pointer && resultNative.category == NativeTypeCategory.Reference && !resultNative.isRValueReference:
                    break;
                case BridgeConversionKind.OwnedValue when resultNative.category == NativeTypeCategory.Record && IsHandlePointer(result, resultNative.id) && !result.isConst:
                    if (export.resultRelease is not StableId releaseId || !exports.TryGetValue(releaseId, out BridgeExport? release)
                        || release.operation.kind != BridgeOperationKind.Destroy || release.operation.ownerType is not StableId releaseOwner || Resolve(releaseOwner).id != resultNative.id
                        || release.parameters.Length != 1 || release.parameters[0].type != result.id || release.resultType is not null)
                        Fail(export, "An owned result requires a matching nonconst native destruction export.");
                    break;
                default:
                    Fail(export, "The result conversion does not match its C carrier and native category.");
                    break;
            }
            if (export.resultConversion != BridgeConversionKind.SourceExpression && export.resultExpression is not null)
                Fail(export, "An ordinary output conversion carries an unrelated source expression.");
            if (export.resultConversion != BridgeConversionKind.OwnedValue && export.resultRelease is not null && export.resultOwnership is null)
                Fail(export, "A borrowed or scalar result cannot declare an owned release.");
            if (operation.kind is BridgeOperationKind.Construct or BridgeOperationKind.DefaultConstruct && export.resultConversion != BridgeConversionKind.OwnedValue)
                Fail(export, "A constructed object must transfer ownership through its paired release.");
        }

        NativeType Resolve(StableId id)
        {
            HashSet<StableId> visited = [];
            NativeType type = module.source.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visited.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("The native bridge type chain is cyclic or incomplete.");
                type = module.source.GetType(element);
            }
            return type;
        }

        bool IsHandlePointer(
            BridgeType type,
            StableId nativeId
        )
            => type.kind == BridgeTypeKind.Pointer && type.elementType is StableId element && types.TryGetValue(element, out BridgeType? handle)
                && handle.kind == BridgeTypeKind.Handle && handle.nativeType is StableId native && Resolve(native).id == nativeId;

        bool Accessible(NativeDeclaration declaration) => declaration.visibility is not ("Private" or "Protected")
            && (declaration.parent is not StableId parent || Accessible(module.source.GetDeclaration(parent)));

        bool IsPolymorphic(
            NativeDeclaration declaration,
            System.Collections.Generic.HashSet<StableId> visited
        ) {
            if (!visited.Add(declaration.id)) return false;
            return declaration.children.Select(module.source.GetDeclaration).Any(child => child.kind == "Function" && child.isVirtual)
                || declaration.baseClasses.Any(item => Resolve(item.type).declaration is StableId parent && IsPolymorphic(module.source.GetDeclaration(parent), visited));
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(
        BridgeExport export,
        string reason
    )
        => throw new InvalidOperationException($"Bridge export '{export.name}': {reason}");
}
