using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BGCS.Analysis.Interop;
using BGCS.Analysis.Bridge;
using BGCS.NativeBridge.Recipes;
using BGCS.Core;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;

namespace BGCS.NativeBridge.Planning;

/// <summary>Plans native operations from an immutable fact graph without loading a parser.</summary>
public sealed class NativeBridgePlanner
{
    private readonly LoweringRegistry m_lowerings;
    private readonly CallableRecipeRegistry m_callableRecipes;
    private readonly ImmutableArray<CppFamilyBinding> m_familyBindings;
    /// <summary>Captures a frozen neutral lowering registry for one planning session.</summary>
    /// <param name="lowerings">The session registry, or an empty registry for built-in representations.</param>
    /// <param name="callableRecipes">Immutable configured exclusions, export naming and invocation wrappers.</param>
    /// <param name="familyBindings">Exact user-asserted finite value families, independent from native storage.</param>
    public NativeBridgePlanner(
        LoweringRegistry? lowerings = null,
        IEnumerable<CallableLoweringRecipe>? callableRecipes = null,
        IEnumerable<CppFamilyBinding>? familyBindings = null
    ) { m_lowerings = lowerings ?? new(); m_callableRecipes = new(callableRecipes); m_familyBindings = CppFamilyBindingValidator.Freeze(familyBindings?.ToImmutableArray() ?? []); }
    /// <summary>Creates checked scalar and opaque-object exports with explicit result and error channels.</summary>
    /// <param name="context">Detached source, target and header facts.</param>
    /// <param name="selectedCallables">Explicit callable selection; null selects accessible concrete declarations.</param>
    /// <param name="cancellationToken">Cancels cooperative planning.</param>
    /// <param name="publicDeclarations">Public declaration roots, independently from metadata-only or required cleanup dependencies.</param>
    /// <param name="virtualProxies">Typed generated virtual adapters derived from the original interface facts.</param>
    /// <param name="variadicAdapters">Finite wrappers with additional types established in the original parse.</param>
    /// <returns>A typed bridge plan ready for candidate emission and independent C reparse.</returns>
    public NativeBridgePlan Plan(
        LoweringContext context,
        IEnumerable<StableId>? selectedCallables = null,
        CancellationToken cancellationToken = default,
        IEnumerable<StableId>? publicDeclarations = null,
        IEnumerable<CppVirtualProxyPlan>? virtualProxies = null,
        IEnumerable<NativeVariadicAdapter>? variadicAdapters = null
    ) {
        ArgumentNullException.ThrowIfNull(context);
        NativeModule module = context.module;
        if (module.language == SourceLanguage.C)
            return new BGCS.NativeBridge.C.AggregatePointerLowering(m_lowerings, m_callableRecipes).Plan(context, selectedCallables, cancellationToken, variadicAdapters);
        StandardLibraryFactReader familyReader = new(m_familyBindings);
        familyReader.ValidateBindings(module);
        if (module.language != SourceLanguage.Cpp) throw new ArgumentException("C++ lowering requires a C++ source graph.", nameof(context));
        if (module.targetId != context.target.targetId.value || module.triple != context.target.triple)
            throw new ArgumentException("The lowering target does not match the detached source facts.", nameof(context));
        if (!Regex.IsMatch(context.exportPrefix, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) || context.exportPrefix.Contains("__", StringComparison.Ordinal))
            throw new ArgumentException("The bridge prefix must be an unreserved C identifier.", nameof(context));
        var errors = new NativeModuleVerifier().Validate(module).Concat(module.diagnostics).Where(diagnostic => diagnostic.isFailure).ToArray();
        if (errors.Length != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.message)));
        HashSet<StableId>? selected = selectedCallables?.ToHashSet();
        HashSet<StableId>? publicRoots = publicDeclarations?.ToHashSet();
        Dictionary<StableId, BridgeType> types = [];
        Dictionary<StableId, NativeTypeLoweringPlan?> customTypes = [];
        HashSet<StableId> resolvingCustomTypes = [];
        Dictionary<StableId, BridgeExport> releases = [];
        List<BridgeExport> exports = [];
        Dictionary<ContentDigest, ControlledSourceFragment> fragments = [];
        Dictionary<StableId, CppResultReleaseRequest> ownedRecipes = [];
        DtoRecordPlanner records = null!;
        records = new(module, Storage, Name, Register, id => Custom(id) is not null || familyReader.FindBinding(module, id) is not null);
        foreach (NativeType enumeration in module.types.Where(type => type.category == NativeTypeCategory.Enumeration))
        {
            if (enumeration.declaration is StableId declarationId && (publicRoots is null || publicRoots.Contains(declarationId))
                && module.GetDeclaration(declarationId) is { isSystemDeclaration: false } declaration && IsAccessible(declaration))
                Storage(enumeration.id);
        }
        foreach (NativeType record in module.types.Where(type => type.category == NativeTypeCategory.Record))
        {
            if (record.declaration is not StableId declarationId || publicRoots is not null && !publicRoots.Contains(declarationId)) continue;
            NativeDeclaration owner = module.GetDeclaration(declarationId);
            if (!IsAccessible(owner) || owner.templateParameters.Length != 0) continue;
            if (records.Plan(record.id) is null && Custom(record.id) is null && !owner.isSystemDeclaration) Handle(record.id);
        }
        foreach (NativeDeclaration declaration in module.declarations.Where(item => item.kind == "Function").OrderBy(item => item.id.value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selected is not null && !selected.Contains(declaration.id)) continue;
            if (selected is null && declaration.isSystemDeclaration) continue;
            CallableLoweringRecipe? callableRecipe = m_callableRecipes.Match(declaration);
            if (callableRecipe?.exclude == true) continue;
            if (declaration.isDeleted || declaration.visibility is "Private" or "Protected")
            {
                if (selected is not null) throw new NotSupportedException($"Callable '{declaration.qualifiedName}' is deleted or inaccessible.");
                continue;
            }
            if (declaration.templateParameters.Length != 0 || declaration.templateKind == "FunctionTemplate")
                throw new NotSupportedException($"Callable '{declaration.qualifiedName}' requires an explicit concrete template instantiation.");
            if (declaration.isVariadic)
            {
                NativeVariadicAdapter[] finite = (variadicAdapters ?? []).Where(adapter => adapter.sourceDeclaration == declaration.id).ToArray();
                if (finite.Length == 0) throw new NotSupportedException($"Variadic callable '{declaration.qualifiedName}' requires an explicitly configured finite source.variadicVariants native adapter or a native shim before C# binding generation.");
                foreach (NativeVariadicAdapter adapter in finite) PlanFinite(declaration, adapter);
                continue;
            }
            if (!declaration.hasPrototype)
                throw new NotSupportedException($"Callable '{declaration.qualifiedName}' requires a fixed native prototype.");
            NativeDeclaration? owner = declaration.parent is StableId parent && module.GetDeclaration(parent).kind == "Class" ? module.GetDeclaration(parent) : null;
            if (owner?.templateParameters.Length > 0)
                throw new NotSupportedException($"Class '{owner.qualifiedName}' requires an explicit concrete template instantiation.");
            if (owner is not null && !IsAccessible(owner)) continue;
            if (declaration.role == "Destructor") { if (owner is not null) Release(owner.type!.Value); continue; }
            if (declaration.role == "Constructor" && owner?.isAbstract == true) continue;
            if (declaration.referenceQualifier == NativeReferenceQualifier.Unknown)
                throw new NotSupportedException($"Member '{declaration.qualifiedName}' has an unresolved native reference qualification.");
            if (declaration.referenceQualifier == NativeReferenceQualifier.RValue)
                throw new NotSupportedException($"Rvalue-qualified member '{declaration.qualifiedName}' requires an explicit ownership-transfer lowering.");
            NativeCallableLoweringPlan? customCallable = m_lowerings.LowerCallable(declaration, context, cancellationToken);
            if (customCallable is not null)
            {
                foreach (BridgeType type in customCallable.types) Register(type);
                exports.Add(CallableRecipeRegistry.Apply(customCallable.export, callableRecipe));
                exports.AddRange(customCallable.additionalExports);
                foreach (ControlledSourceFragment fragment in customCallable.sourceFragments) AddFragment(fragment);
                if (callableRecipe is not null) foreach (ControlledSourceFragment fragment in callableRecipe.sourceFragments) AddFragment(fragment);
                continue;
            }
            List<BridgeParameter> parameters = [];
            List<BridgeArgumentGroup> argumentGroups = [];
            bool hasExpandedArguments = false;
            StableId? self = null;
            BridgeOperationKind kind = declaration.role == "Constructor" ? BridgeOperationKind.Construct : owner is null ? BridgeOperationKind.FreeCall
                : declaration.isStatic ? BridgeOperationKind.StaticCall : BridgeOperationKind.InstanceCall;
            if (kind == BridgeOperationKind.InstanceCall)
            {
                self = new("bridge-parameter:" + declaration.id.value + ":self");
                parameters.Add(new(self.Value, "bgcs_self", Pointer(Handle(owner!.type!.Value), declaration.isConst).id, owner.type.Value, BridgeConversionKind.PointerCast));
            }
            int index = 0;
            foreach (NativeDeclaration parameter in declaration.children.Select(module.GetDeclaration).Where(child => child.kind == "Parameter"))
            {
                NativeType native = Resolve(parameter.type ?? throw new InvalidOperationException("A callable parameter has no type fact."));
                BridgeConversionKind conversion = native.category == NativeTypeCategory.Reference ? native.isRValueReference ? BridgeConversionKind.Move : BridgeConversionKind.Dereference
                    : native.category == NativeTypeCategory.Record
                        ? familyReader.Read(module, native.id)?.family == StandardValueKind.UniquePointer ? BridgeConversionKind.Move : BridgeConversionKind.Dereference
                        : native.category == NativeTypeCategory.Pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast;
                NativeTypeLoweringPlan? custom = Custom(parameter.type!.Value);
                if (custom is null && native.category == NativeTypeCategory.Record && records.Plan(native.id) is not null)
                    conversion = BridgeConversionKind.RecordCopy;
                if (custom is { parameterBindings.IsEmpty: false })
                {
                    hasExpandedArguments = true;
                    if (custom.parameterExpression is null || custom.parameterConversion != BridgeConversionKind.SourceExpression)
                        throw new InvalidOperationException("Multiple C input carriers require their explicit native argument expression.");
                    ImmutableArray<BridgeArgumentBinding>.Builder inputs = ImmutableArray.CreateBuilder<BridgeArgumentBinding>();
                    int position = index++;
                    foreach (CppAbiInput input in custom.parameterBindings)
                    {
                        StableId inputId = new("bridge-parameter:" + parameter.id.value + ":abi:" + input.name);
                        BridgeType carrier = types[input.type];
                        parameters.Add(new(inputId, "arg" + position.ToString(CultureInfo.InvariantCulture) + "_" + input.name, input.type, parameter.type.Value,
                            carrier.kind == BridgeTypeKind.Pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast));
                        inputs.Add(new(input.name, inputId));
                    }
                    argumentGroups.Add(new(parameter.id, parameter.type.Value, inputs.ToImmutable(), custom.parameterExpression));
                    continue;
                }
                if (custom is not null) conversion = custom.parameterConversion;
                BridgeParameter direct = new(new("bridge-parameter:" + parameter.id.value), "arg" + (index++).ToString(CultureInfo.InvariantCulture), Lower(parameter.type!.Value).id, parameter.type.Value, conversion)
                { conversionExpression = custom?.parameterExpression, managedProjection = custom?.managedProjection };
                parameters.Add(direct);
                argumentGroups.Add(new(parameter.id, parameter.type.Value, [new("value", direct.id)], null));
            }
            BridgeExport export = new(new("bridge-export:" + declaration.id.value), Name(declaration.qualifiedName, declaration.id), parameters.ToImmutableArray(),
                new(kind, declaration.id, owner?.type, self) { argumentGroups = hasExpandedArguments ? argumentGroups.ToImmutableArray() : [] })
            { documentation = declaration.documentation };
            if (kind == BridgeOperationKind.Construct)
                export = export with { resultType = Pointer(Handle(owner!.type!.Value), false).id, nativeResultType = owner.type, resultConversion = BridgeConversionKind.OwnedValue, resultRelease = Release(owner.type!.Value).id };
            else if (declaration.returnType is StableId returnType && Resolve(returnType).primitiveKind != "Void")
            {
                NativeType result = Resolve(returnType);
                NativeTypeLoweringPlan? custom = Custom(returnType);
                BridgeType? resultRecord = custom is null && result.category == NativeTypeCategory.Record ? records.Plan(result.id) : null;
                if (custom?.resultConversion == BridgeConversionKind.OwnedValue)
                    throw new NotSupportedException("Custom owned result lowerings require an explicit callable plan with its matching release export.");
                if (result.category == NativeTypeCategory.Reference && result.isRValueReference)
                    throw new NotSupportedException($"Rvalue-reference result '{declaration.qualifiedName}' requires an explicit ownership-transfer lowering.");
                export = export with
                {
                    resultType = resultRecord?.id ?? Lower(returnType).id,
                    nativeResultType = returnType,
                    resultConversion = custom?.resultConversion ?? (result.category == NativeTypeCategory.Reference ? BridgeConversionKind.ReferenceAddress
                    : resultRecord is not null ? BridgeConversionKind.RecordCopy : result.category == NativeTypeCategory.Record ? BridgeConversionKind.OwnedValue
                    : result.category == NativeTypeCategory.Pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast),
                    resultRelease = custom is null && resultRecord is null && result.category == NativeTypeCategory.Record ? Release(result.id).id : null
                };
                export = export with { resultExpression = custom?.resultExpression, managedResultProjection = custom?.managedProjection };
                if (custom?.ownedResultRelease is CppResultReleaseRequest release)
                {
                    LoweringRecipeCompiler.ValidateRelease(release);
                    if (custom.resultConversion != BridgeConversionKind.SourceExpression || custom.resultExpression is null)
                        throw new InvalidOperationException("An explicit owned native recipe requires its fingerprinted allocation/conversion expression.");
                    ownedRecipes.Add(export.id, release);
                }
            }
            exports.Add(CallableRecipeRegistry.Apply(export, callableRecipe));
            if (callableRecipe is not null) foreach (ControlledSourceFragment fragment in callableRecipe.sourceFragments) AddFragment(fragment);
        }
        if (selected is not null && selected.Any(id => module.GetDeclaration(id).kind != "Function"))
            throw new ArgumentException("A selected bridge declaration is not callable.", nameof(selectedCallables));
        foreach (BridgeType handle in types.Values.Where(type => type.kind == BridgeTypeKind.Handle).ToArray())
        {
            NativeDeclaration owner = module.GetDeclaration(module.GetType(handle.nativeType!.Value).declaration!.Value);
            foreach (NativeBaseClass baseClass in owner.baseClasses.Where(baseClass => baseClass.visibility == "Public"))
            {
                NativeType destination = Resolve(baseClass.type);
                if (destination.category != NativeTypeCategory.Record || !IsAccessible(module.GetDeclaration(destination.declaration!.Value))) continue;
                StableId self = new("bridge-parameter:" + owner.id.value + ":base:" + destination.id.value);
                StableId exportId = new("bridge-export:" + owner.id.value + ":base:" + destination.id.value);
                exports.Add(new(exportId, Name(owner.qualifiedName + "_as_" + module.GetDeclaration(destination.declaration.Value).qualifiedName, exportId),
                    [new(self, "bgcs_self", Pointer(handle, false).id, handle.nativeType.Value, BridgeConversionKind.PointerCast)],
                    new(BridgeOperationKind.BaseCast, null, handle.nativeType.Value, self) { castTarget = destination.id })
                { resultType = Pointer(Handle(destination.id), false).id, nativeResultType = destination.id, resultConversion = BridgeConversionKind.PointerCast });
                if (IsPolymorphic(module.GetDeclaration(destination.declaration.Value), []))
                {
                    StableId downcastId = new("bridge-export:" + owner.id.value + ":from-base:" + destination.id.value);
                    StableId sourceParameter = new("bridge-parameter:" + downcastId.value);
                    exports.Add(new(downcastId, Name(owner.qualifiedName + "_from_" + module.GetDeclaration(destination.declaration.Value).qualifiedName, downcastId),
                        [new(sourceParameter, "bgcs_self", Pointer(Handle(destination.id), false).id, destination.id, BridgeConversionKind.PointerCast)],
                        new(BridgeOperationKind.CheckedDerivedCast, null, destination.id, sourceParameter) { castTarget = handle.nativeType.Value })
                    { resultType = Pointer(handle, false).id, nativeResultType = handle.nativeType.Value, resultConversion = BridgeConversionKind.PointerCast });
                }
            }
        }
        StandardValuePlanner standards = new(module, Lower, Release, Pointer, Register, Name, Storage, Handle, familyReader);
        HashSet<StableId> processedStandards = [];
        while (types.Values.FirstOrDefault(type => type.kind == BridgeTypeKind.Handle && !processedStandards.Contains(type.id)) is BridgeType standardHandle)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processedStandards.Add(standardHandle.id);
            exports.AddRange(standards.Plan(standardHandle));
        }
        foreach (BridgeType handle in types.Values.Where(type => type.kind == BridgeTypeKind.Handle).ToArray())
        {
            NativeDeclaration owner = module.GetDeclaration(module.GetType(handle.nativeType!.Value).declaration!.Value);
            if (owner.isSystemDeclaration || !owner.isCompleteDefinition || owner.isAbstract || !IsAccessible(owner)
                || publicRoots is not null && !publicRoots.Contains(owner.id)
                || records.Plan(handle.nativeType!.Value) is not null
                || owner.templateParameters.Length != 0 || owner.isDefaultConstructible != true
                || owner.children.Select(module.GetDeclaration).Any(child => child.role == "Constructor")
                || familyReader.Read(module, handle.nativeType.Value) is not null) continue;
            StableId id = new("bridge-default-construct:" + handle.nativeType.Value.value);
            exports.Add(new(id, Name(owner.qualifiedName + "_default_construct", id), [],
                new(BridgeOperationKind.DefaultConstruct, null, handle.nativeType.Value, null))
            {
                resultType = Pointer(handle, false).id,
                nativeResultType = handle.nativeType.Value,
                resultConversion = BridgeConversionKind.OwnedValue,
                resultRelease = Release(handle.nativeType.Value).id
            });
        }
        exports.AddRange(releases.Values);
        foreach ((StableId exportId, CppResultReleaseRequest request) in ownedRecipes)
        {
            NativeDeclaration[] candidates = module.declarations.Where(declaration => declaration.kind == "Function"
                && declaration.qualifiedName == request.nativeFunction.TrimStart(':')).ToArray();
            if (candidates.Length != 1) throw new InvalidOperationException("An owned recipe release must identify one exact native function declaration.");
            NativeDeclaration releaseDeclaration = candidates[0];
            if (releaseDeclaration.returnType is not StableId releaseReturn || Resolve(releaseReturn).primitiveKind != "Void")
                throw new InvalidOperationException("An owned recipe release must return native void; status-dependent cleanup requires an explicit native adapter.");
            BridgeExport? release = exports.SingleOrDefault(candidate => candidate.operation.declaration == releaseDeclaration.id);
            if (release is null)
            {
                NativeBridgePlan dependencies = new NativeBridgePlanner(m_lowerings, m_callableRecipes.recipes, m_familyBindings).Plan(context, [releaseDeclaration.id], cancellationToken,
                    publicDeclarations: []);
                foreach (BridgeType dependency in dependencies.types) Register(dependency);
                foreach (ControlledSourceFragment fragment in dependencies.sourceFragments) AddFragment(fragment);
                foreach (BridgeExport dependency in dependencies.exports)
                {
                    BridgeExport? existing = exports.SingleOrDefault(candidate => candidate.id == dependency.id);
                    if (existing is null) exports.Add(dependency);
                    else if (System.Text.Json.JsonSerializer.Serialize(existing) != System.Text.Json.JsonSerializer.Serialize(dependency))
                        throw new InvalidOperationException("Resolving owned recipe cleanup changed a previously planned export contract.");
                }
                release = exports.SingleOrDefault(candidate => candidate.operation.declaration == releaseDeclaration.id);
            }
            if (release is null) throw new InvalidOperationException("An owned recipe release was excluded or did not produce an exact typed export.");
            int index = exports.FindIndex(candidate => candidate.id == exportId);
            exports[index] = exports[index] with
            {
                resultRelease = release.id,
                resultOwnership = new(release.id, new("native-recipe-allocator:" + request.allocatorDomain), request.releaseThreadDomain, request.evidence)
            };
        }
        if (exports.Select(export => export.name).Distinct(StringComparer.Ordinal).Count() != exports.Count)
            throw new InvalidOperationException("Bridge export names collide.");
        NativeBridgePlan planned = new(module, context.exportPrefix, context.headers, types.Values.OrderBy(type => type.id.value, StringComparer.Ordinal).ToImmutableArray(),
            exports.OrderBy(export => export.name, StringComparer.Ordinal).ToImmutableArray())
        {
            sourceFragments = fragments.Values.OrderBy(fragment => fragment.origin, StringComparer.Ordinal).ThenBy(fragment => fragment.digest.value, StringComparer.Ordinal).ToImmutableArray(),
            familyBindings = m_familyBindings,
            sourceConventions = context.target.sourceConventions
        };
        return VirtualProxyBridgePlanner.Apply(planned, virtualProxies ?? []);

        void PlanFinite(
            NativeDeclaration declaration,
            NativeVariadicAdapter adapter
        ) {
            NativeDeclaration[] fixedParameters = declaration.children.Select(module.GetDeclaration).Where(child => child.kind == "Parameter").ToArray();
            if (!declaration.hasPrototype || declaration.returnType is null || declaration.templateParameters.Length != 0
                || declaration.parent is StableId parent && module.GetDeclaration(parent).kind == "Class"
                || adapter.fixedParameterCount != fixedParameters.Length || adapter.argumentTypes.IsDefault
                || adapter.argumentTypes.Length != adapter.additionalArguments.Length
                || new BGCS.Analysis.Types.NativeTypeFormatter().CallableSignature(module, declaration) != adapter.sourceSignature)
                throw new InvalidOperationException("A finite variadic operation lost its exact original fixed prefix or type query facts.");
            List<BridgeParameter> parameters = [];
            StableId exportId = new("bridge-finite:" + declaration.id.value + ":" + adapter.wrapper);
            StableId[] nativeTypes = fixedParameters.Select(parameter => parameter.type!.Value).Concat(adapter.argumentTypes).ToArray();
            for (int index = 0; index < nativeTypes.Length; index++)
            {
                StableId nativeId = nativeTypes[index];
                NativeType native = Resolve(nativeId);
                NativeTypeLoweringPlan? custom = Custom(nativeId);
                if (custom is { parameterBindings.IsEmpty: false })
                    throw new NotSupportedException("A finite variadic argument with multiple custom carriers requires an explicit callable shim.");
                BridgeConversionKind conversion = custom?.parameterConversion ?? (native.category == NativeTypeCategory.Reference
                    ? native.isRValueReference ? BridgeConversionKind.Move : BridgeConversionKind.Dereference
                    : native.category == NativeTypeCategory.Record ? records.Plan(native.id) is not null ? BridgeConversionKind.RecordCopy : BridgeConversionKind.Dereference
                    : native.category == NativeTypeCategory.Pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast);
                parameters.Add(new(new(exportId.value + ":parameter:" + index), "arg" + index, Lower(nativeId).id, nativeId, conversion)
                { conversionExpression = custom?.parameterExpression, managedProjection = custom?.managedProjection });
            }
            BridgeExport export = new(exportId, Name(adapter.wrapper, exportId), parameters.ToImmutableArray(),
                new(BridgeOperationKind.FiniteVariadicCall, declaration.id, null, null) { finiteVariadic = adapter })
            { documentation = declaration.documentation };
            NativeType result = Resolve(declaration.returnType.Value);
            if (result.primitiveKind != "Void")
            {
                NativeTypeLoweringPlan? custom = Custom(declaration.returnType.Value);
                if (custom?.resultConversion == BridgeConversionKind.OwnedValue)
                    throw new NotSupportedException("A finite variadic owned custom result requires an explicit callable shim with its exact cleanup protocol.");
                BridgeType? record = custom is null && result.category == NativeTypeCategory.Record ? records.Plan(result.id) : null;
                export = export with
                {
                    resultType = record?.id ?? Lower(declaration.returnType.Value).id,
                    nativeResultType = declaration.returnType,
                    resultConversion = custom?.resultConversion ?? (result.category == NativeTypeCategory.Reference ? BridgeConversionKind.ReferenceAddress
                        : record is not null ? BridgeConversionKind.RecordCopy : result.category == NativeTypeCategory.Record ? BridgeConversionKind.OwnedValue
                        : result.category == NativeTypeCategory.Pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast),
                    resultRelease = custom is null && record is null && result.category == NativeTypeCategory.Record ? Release(result.id).id : null,
                    resultExpression = custom?.resultExpression,
                    managedResultProjection = custom?.managedProjection
                };
                if (custom?.ownedResultRelease is CppResultReleaseRequest release)
                {
                    LoweringRecipeCompiler.ValidateRelease(release);
                    if (custom.resultConversion != BridgeConversionKind.SourceExpression || custom.resultExpression is null)
                        throw new InvalidOperationException("An explicit owned native recipe requires its fingerprinted allocation/conversion expression.");
                    ownedRecipes.Add(export.id, release);
                }
            }
            CallableLoweringRecipe? recipe = m_callableRecipes.Match(declaration);
            exports.Add(CallableRecipeRegistry.Apply(export, recipe));
            if (recipe is not null) foreach (ControlledSourceFragment fragment in recipe.sourceFragments) AddFragment(fragment);
        }

        NativeType Resolve(StableId id)
        {
            HashSet<StableId> visited = [];
            NativeType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visited.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("A native alias graph is cyclic or incomplete.");
                type = module.GetType(element);
            }
            return type;
        }

        BridgeType Lower(StableId id)
        {
            NativeTypeLoweringPlan? custom = Custom(id);
            if (custom is not null) return types[custom.type];
            NativeType original = module.GetType(id);
            NativeType native = Resolve(id);
            if (native.category == NativeTypeCategory.Record) return Pointer(records.Plan(native.id) ?? Handle(native.id), records.Plan(native.id) is not null || HasConst(id));
            if (native.category is NativeTypeCategory.Reference or NativeTypeCategory.Pointer)
            {
                if (Custom(native.elementType!.Value) is not null)
                    throw new NotSupportedException("A pointer or reference to a custom value representation needs an explicit callable lowering; storage sizes alone cannot justify a reinterpret cast.");
                if (Resolve(native.elementType!.Value).primitiveKind == "Bool")
                    throw new NotSupportedException("A native bool pointer or reference requires an explicit normalized buffer lowering.");
                NativeType pointee = Resolve(native.elementType.Value);
                return Pointer(pointee.category == NativeTypeCategory.Record ? Handle(pointee.id) : Storage(native.elementType.Value), HasConst(native.elementType.Value));
            }
            return Storage(original.id);
        }

        BridgeType Storage(StableId id)
        {
            NativeTypeLoweringPlan? custom = Custom(id);
            if (custom is not null) return types[custom.type];
            NativeType native = Resolve(id);
            if (native.category == NativeTypeCategory.Record) return records.Plan(native.id) ?? Handle(native.id);
            if (native.category == NativeTypeCategory.Array && native.arrayLength is > 0 && native.elementType is StableId arrayElement)
            {
                StableId arrayId = new("bridge-array:" + native.id.value);
                if (!types.TryGetValue(arrayId, out BridgeType? array))
                    types.Add(arrayId, array = new(arrayId, BridgeTypeKind.FixedArray, string.Empty)
                    { nativeType = native.id, arrayLength = native.arrayLength, elementType = Storage(arrayElement).id });
                return array;
            }
            if (native.category == NativeTypeCategory.Pointer) return Lower(native.id);
            if (native.category == NativeTypeCategory.Function)
            {
                if (!native.hasPrototype || native.returnType is null || native.parameterTypes.IsDefault
                    || native.callingConvention is not ("C" or "Default"))
                    throw new NotSupportedException($"Native callback '{native.spelling}' requires a complete supported C calling convention and prototype.");
                StableId functionId = new("bridge-function:" + native.id.value);
                if (types.TryGetValue(functionId, out BridgeType? existing)) return existing;
                StableId result = CallbackStorage(native.returnType.Value, isResult: true).id;
                ImmutableArray<StableId> parameters = native.parameterTypes.Select(parameter => CallbackStorage(parameter, isResult: false).id).ToImmutableArray();
                BridgeType function = new(functionId, BridgeTypeKind.Function, Name("callback", native.id))
                {
                    nativeType = native.id,
                    returnType = result,
                    parameterTypes = parameters,
                    callingConvention = native.callingConvention,
                    isVariadic = native.isVariadic,
                    hasPrototype = native.hasPrototype
                };
                types.Add(functionId, function);
                return function;
            }
            if (native.category == NativeTypeCategory.Enumeration)
            {
                BridgeType underlying = Storage(native.elementType!.Value);
                StableId enumId = new("bridge-enum:" + native.id.value);
                if (!types.TryGetValue(enumId, out BridgeType? enumeration))
                    types.Add(enumId, enumeration = new(enumId, BridgeTypeKind.Enumeration, Name(native.spelling, native.id)) { nativeType = native.id, elementType = underlying.id });
                return enumeration;
            }
            if (native.category != NativeTypeCategory.Primitive)
                throw new NotSupportedException($"Native type '{native.spelling}' requires a registered concrete type lowering.");
            string name = native.primitiveKind switch
            {
                "Void" => "void",
                "Bool" when native.size == 1 => "uint8_t",
                "Float" when native.size == 4 => "float",
                "Double" when native.size == 8 => "double",
                _ when native.size is 1 or 2 or 4 or 8 && native.isSigned is not null => (native.isSigned.Value ? "int" : "uint") + (native.size.Value * 8).ToString(CultureInfo.InvariantCulture) + "_t",
                _ => throw new NotSupportedException($"Native scalar '{native.spelling}' has no checked fixed-width bridge conversion.")
            };
            StableId bridgeId = new("bridge-scalar:" + name);
            if (!types.TryGetValue(bridgeId, out BridgeType? bridge)) types.Add(bridgeId, bridge = new(bridgeId, BridgeTypeKind.Scalar, name) { nativeType = native.id });
            return bridge;
        }

        BridgeType CallbackStorage(
            StableId id,
            bool isResult
        ) {
            NativeType type = Resolve(id);
            if (Custom(id) is not null || type.category is NativeTypeCategory.Record or NativeTypeCategory.Reference or NativeTypeCategory.Array
                || type.category == NativeTypeCategory.Primitive && (type.primitiveKind == "Bool" || !isResult && type.primitiveKind == "Void"))
                throw new NotSupportedException($"Native callback value '{type.spelling}' requires a typed callback conversion adapter; ordinary C++ argument conversions cannot run inside a function-pointer cast.");
            return Storage(id);
        }

        BridgeType Handle(StableId id)
        {
            NativeType native = Resolve(id);
            if (native.category != NativeTypeCategory.Record || native.declaration is not StableId declarationId || !IsAccessible(module.GetDeclaration(declarationId)))
                throw new NotSupportedException($"Native type '{native.spelling}' has no accessible opaque object identity.");
            StableId bridgeId = new("bridge-handle:" + native.id.value);
            if (!types.TryGetValue(bridgeId, out BridgeType? bridge)) types.Add(bridgeId, bridge = new(bridgeId, BridgeTypeKind.Handle, Name(module.GetDeclaration(declarationId).qualifiedName + "_object", native.id)) { nativeType = native.id });
            return bridge;
        }

        BridgeType Pointer(
            BridgeType element,
            bool isConst
        ) {
            StableId id = new("bridge-pointer:" + (isConst ? "const:" : string.Empty) + element.id.value);
            if (!types.TryGetValue(id, out BridgeType? pointer)) types.Add(id, pointer = new(id, BridgeTypeKind.Pointer, string.Empty) { elementType = element.id, isConst = isConst });
            return pointer;
        }

        bool HasConst(StableId id)
        {
            NativeType type = module.GetType(id);
            if (type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true) return true;
            return type.category is NativeTypeCategory.Qualified or NativeTypeCategory.Alias && type.elementType is StableId element && HasConst(element);
        }

        bool IsAccessible(NativeDeclaration declaration)
        {
            if (declaration.visibility is "Private" or "Protected") return false;
            return declaration.parent is not StableId parent || IsAccessible(module.GetDeclaration(parent));
        }

        bool IsPolymorphic(
            NativeDeclaration declaration,
            HashSet<StableId> visited
        ) {
            if (!visited.Add(declaration.id)) return false;
            if (declaration.children.Select(module.GetDeclaration).Any(child => child.kind == "Function" && child.isVirtual)) return true;
            return declaration.baseClasses.Any(baseClass => Resolve(baseClass.type).declaration is StableId parent && IsPolymorphic(module.GetDeclaration(parent), visited));
        }

        BridgeExport Release(StableId nativeId)
        {
            NativeType native = Resolve(nativeId);
            if (releases.TryGetValue(native.id, out BridgeExport? release)) return release;
            NativeDeclaration owner = module.GetDeclaration(native.declaration!.Value);
            NativeDeclaration? destructor = owner.children.Select(module.GetDeclaration).FirstOrDefault(child => child.role == "Destructor");
            if (destructor is { isDeleted: true } || destructor?.visibility is "Private" or "Protected")
                throw new NotSupportedException($"Owned result '{owner.qualifiedName}' has no accessible destructor.");
            StableId parameter = new("bridge-parameter:" + native.id.value + ":destroy");
            release = new(new("bridge-release:" + native.id.value), Name(owner.qualifiedName + "_destroy", native.id),
                [new(parameter, "bgcs_self", Pointer(Handle(native.id), false).id, native.id, BridgeConversionKind.PointerCast)],
                new(BridgeOperationKind.Destroy, destructor?.id, native.id, parameter));
            releases.Add(native.id, release);
            return release;
        }

        string Name(
            string hint,
            StableId identity
        ) {
            string normalized = Regex.Replace(hint, "[^A-Za-z0-9]+", "_", RegexOptions.CultureInvariant).Trim('_');
            if (normalized.Length > 80) normalized = normalized[..80].TrimEnd('_');
            if (normalized.Length == 0) normalized = "value";
            string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(hint + "|" + identity.value)))[..16];
            return context.exportPrefix.TrimEnd('_') + "_" + normalized + "_" + hash;
        }

        NativeTypeLoweringPlan? Custom(StableId id)
        {
            if (customTypes.TryGetValue(id, out NativeTypeLoweringPlan? cached)) return cached;
            if (!resolvingCustomTypes.Add(id)) throw new InvalidOperationException("A custom lowering alias graph is cyclic.");
            try
            {
                NativeTypeLoweringPlan? plan = m_lowerings.LowerType(new(module, id), context, cancellationToken);
                NativeType native = module.GetType(id);
                if (plan is null && native.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && native.elementType is StableId element)
                    plan = Custom(element);
                customTypes.Add(id, plan);
                if (plan is not null)
                {
                    if (familyReader.FindBinding(module, id) is not null)
                        throw new InvalidOperationException("An explicit family binding conflicts with a custom value representation for the same native type.");
                    if (plan.types.IsDefault || plan.sourceFragments.IsDefault || plan.parameterBindings.IsDefault)
                        throw new InvalidOperationException("A type lowering returned uninitialized representation or input collections.");
                    foreach (BridgeType type in plan.types) Register(type);
                    if (plan.parameterBindings.Any(binding => !types.ContainsKey(binding.type)))
                        throw new InvalidOperationException("A type lowering omitted an explicit input C carrier from its graph.");
                    foreach (ControlledSourceFragment fragment in plan.sourceFragments) AddFragment(fragment);
                    if (!types.ContainsKey(plan.type)) throw new InvalidOperationException("A type lowering omitted its primary C representation.");
                }
                return plan;
            }
            finally { resolvingCustomTypes.Remove(id); }
        }

        void Register(BridgeType type)
        {
            if (types.TryGetValue(type.id, out BridgeType? previous) && previous != type)
            {
                if (previous.kind == BridgeTypeKind.Scalar && type.kind == BridgeTypeKind.Scalar && previous.cName == type.cName
                    && previous.elementType is null && type.elementType is null && previous.isConst == type.isConst) return;
                throw new InvalidOperationException("C representation identities conflict between lowerings.");
            }
            types[type.id] = type;
        }

        void AddFragment(ControlledSourceFragment fragment)
        {
            new ControlledSourceFragmentVerifier().Validate(fragment, module, context.headers, []);
            if (fragment.kind != SourceFragmentKind.TranslationUnit) throw new InvalidOperationException("A native helper declaration must be a translation-unit fragment.");
            if (fragments.TryGetValue(fragment.digest, out ControlledSourceFragment? previous) && previous != fragment)
                throw new InvalidOperationException("Native helper fragments with the same content identity declare different dependencies or evidence.");
            fragments[fragment.digest] = fragment;
        }
    }
}
