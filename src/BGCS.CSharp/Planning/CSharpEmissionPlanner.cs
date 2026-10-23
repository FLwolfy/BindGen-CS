using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Analysis.Safety;
using BGCS.Analysis.Calls;
using BGCS.Core.Calls;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;

namespace BGCS.CSharp.Planning;

/// <summary>Resolves checked C# ABI signatures and storage before any source is written.</summary>
public sealed class CSharpEmissionPlanner
{
    /// <summary>Plans C# carriers and imports from frozen native and managed models.</summary>
    /// <param name="model">The managed API projection.</param>
    /// <param name="request">The selected backend choices.</param>
    /// <param name="cancellationToken">Cancels cooperative planning.</param>
    /// <returns>A frozen source-emission plan.</returns>
    /// <exception cref="NotSupportedException">A shape has no proven carrier or calling ABI.</exception>
    public CSharpEmissionPlan Plan(
        ManagedApiModel model,
        EmissionRequest request,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.sink);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.libraryName);
        if (model.interop.types.Any(type => type.sourceNativeType is not null && type.sourceGeneratedType is not null)
            || model.interop.functions.Any(function => function.sourceOwnerType is not null && function.sourceGeneratedOwnerType is not null))
            throw new ArgumentException("A declaration cannot claim both original-native and generated-bridge owner identities.", nameof(model));
        new BGCS.Analysis.Bridge.VirtualProxyProtocolVerifier().Verify(model.interop);
        new BGCS.Analysis.Interop.NativeFunctionAliasVerifier().Verify(model.interop);
        CSharpNames.Namespace(model.@namespace);
        CSharpNames.Namespace(request.runtimeNamespace);
        if (request.additionalUsings.IsDefault) throw new ArgumentException("Additional namespaces must be initialized.");
        if (request.additionalRuntimeSourceFeatures.IsDefault) throw new ArgumentException("Additional Runtime source features must be initialized.");
        if (!request.includeRuntimeSource && !request.additionalRuntimeSourceFeatures.IsEmpty)
            throw new ArgumentException("Additional Runtime source features require source delivery.");
        if (request.additionalRuntimeSourceFeatures.Distinct(StringComparer.Ordinal).Count() != request.additionalRuntimeSourceFeatures.Length
            || request.additionalRuntimeSourceFeatures.Any(feature => !Emission.RuntimeSourceCatalog.GetFeatureIds().Contains(feature, StringComparer.Ordinal)))
            throw new ArgumentException("Additional Runtime source features must be unique exact registered catalog identities.");
        foreach (string ns in request.additionalUsings) CSharpNames.Namespace(ns);
        CSharpNames.Identifier(model.name);
        if (!Enum.IsDefined(model.style)) throw new ArgumentException("The managed API lifetime style is undefined.");
        string contextName = CSharpNames.Identifier(model.style == ManagedApiStyle.Static ? model.name.TrimStart('@') + "Context" : model.name);
        if (model.name.TrimStart('@') is "RawApi" or "NativeImports" or "StaticNativeAdapter")
            throw new ArgumentException("The API context name conflicts with one of its required nested implementation types.");
        if (request.framework != "net10.0") throw new NotSupportedException("BGCS requires net10.0 consumers.");
        if (model.interop.pointerSize is not (4 or 8)) throw new NotSupportedException("A verified 32-bit or 64-bit pointer representation is required.");
        if (!Enum.IsDefined(model.interop.byteOrder)) throw new NotSupportedException("The native ABI contains an undefined byte-order fact.");
        if (model.interop.diagnostics.Any(diagnostic => diagnostic.isFailure))
            throw new InvalidOperationException("Native ABI errors must be resolved before C# planning.");
        var runtime = request.managedRuntime ?? throw new NotSupportedException("A source backend requires an explicitly resolved managed runtime profile.");
        CallPlanValidator.VerifyProfile(model.interop, runtime);
        {
            if (!runtime.importModes.Contains(request.importMode))
                throw new NotSupportedException("The resolved managed runtime profile does not admit this import mode.");
            if (runtime.moduleMechanism == BGCS.Core.Targets.NativeModuleMechanism.StaticAdapter && request.importMode == BindingImportMode.FunctionTable)
            {
                StaticExportTableDescriptor table = request.staticExportTable ?? throw new NotSupportedException("A static function table requires an explicit generated native adapter contract.");
                _ = new ContentDigest(table.abiIdentity);
                string[] adapterSymbols = [table.getExportSymbol, table.getRevisionSymbol, table.getCountSymbol, table.getPointerSizeSymbol, table.getAbiIdentitySymbol];
                if (table.revision != 1 || adapterSymbols.Distinct(StringComparer.Ordinal).Count() != adapterSymbols.Length
                    || adapterSymbols.Any(symbol => !System.Text.RegularExpressions.Regex.IsMatch(symbol, "^[A-Za-z_][A-Za-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
                    throw new InvalidOperationException("The static native adapter protocol or symbol identities are invalid.");
                if (!table.exportNames.SequenceEqual(model.interop.functions.Select(function => function.exportName), StringComparer.Ordinal))
                    throw new InvalidOperationException("The static native adapter export order differs from the frozen C ABI.");
            }
        }
        if (request.staticExportTable is not null && (request.managedRuntime?.moduleMechanism != BGCS.Core.Targets.NativeModuleMechanism.StaticAdapter || request.importMode != BindingImportMode.FunctionTable))
            throw new ArgumentException("A static native table contract is only consumed by a static FunctionTable profile.");
        Dictionary<StableId, ManagedType> managed = model.types.ToDictionary(type => type.nativeType);
        Dictionary<StableId, InteropFunction> nativeExports = model.interop.functions.ToDictionary(function => function.id);
        Dictionary<StableId, CSharpTypePlan> types = [];
        AggregateAbiPlanner aggregates = new(model.interop, runtime, request.callPlans);
        Dictionary<StableId, CSharpOpaqueCallablePlan> opaqueCallables = [];
        HashSet<StableId> active = [];
        HashSet<string> names = new(StringComparer.Ordinal) { model.name.TrimStart('@'), contextName.TrimStart('@') };
        foreach (InteropType type in model.interop.types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Resolve(type.id);
        }
        ImmutableArray<CSharpStoragePlan>.Builder storage = ImmutableArray.CreateBuilder<CSharpStoragePlan>();
        foreach (CSharpTypePlan type in types.Values.Where(type => type.declaredName is not null).OrderBy(type => type.nativeType.value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            InteropType native = model.interop.GetType(type.nativeType);
            try { storage.Add(PlanStorage(native, type.declaredName!) with { documentation = native.documentation }); }
            catch (NotSupportedException failure) when (failure is not CSharpPlanningException)
            { throw new CSharpPlanningException(native.id, native.source, failure.Message, failure); }
        }
        ImmutableArray<CSharpManagedEnumPlan> managedEnums = new ManagedEnumPlanner().Plan(model.enums, cancellationToken);
        foreach (CSharpManagedEnumPlan declaration in managedEnums)
            if (!names.Add(declaration.name.TrimStart('@'))) throw new ArgumentException("A managed enum name conflicts with the API context or a native storage declaration.");
        foreach (CSharpStoragePlan declaration in storage)
            if (declaration.alignmentProbeName is string probe && !names.Add(probe))
                throw new ArgumentException("A storage declaration conflicts with a required internal alignment probe.");
        ImmutableArray<CSharpMethodPlan>.Builder methods = ImmutableArray.CreateBuilder<CSharpMethodPlan>();
        ImmutableArray<CSharpValueParameterOmissionPlan>.Builder valueOmissions = ImmutableArray.CreateBuilder<CSharpValueParameterOmissionPlan>();
        Dictionary<StableId, CSharpStoragePlan> valueStorage = storage.ToDictionary(item => item.nativeType);
        ManagedRecipeProjectionValidator projectionValidator = new();
        Dictionary<string, CSharpRecipeProjectionPlan> recipeProjections = new(StringComparer.Ordinal);
        ImmutableArray<CSharpRecipeAliasPlan>.Builder recipeAliases = ImmutableArray.CreateBuilder<CSharpRecipeAliasPlan>();
        HashSet<string> recipeNames = new(StringComparer.Ordinal);
        foreach (ManagedRecipeHelper helper in model.recipeHelpers)
        {
            ArgumentNullException.ThrowIfNull(helper);
            ArgumentException.ThrowIfNullOrWhiteSpace(helper.id);
            CSharpRecipeProjectionPlan projection = projectionValidator.Plan(helper.projection, helper.cAbiType, model.interop.pointerSize);
            recipeProjections[projection.id] = projection;
            string name = System.Text.RegularExpressions.Regex.Replace(helper.id, "[^A-Za-z0-9_]", "_", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (name.Length == 0) throw new InvalidOperationException("A configured managed recipe helper identity has no portable characters.");
            if (!recipeNames.Add(name)) throw new InvalidOperationException("Configured managed recipe helper names collide.");
            recipeAliases.Add(new(name, projection.id));
        }
        foreach (ManagedMember member in model.methods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string methodName = CSharpNames.Identifier(member.name);
            if (!nativeExports.TryGetValue(member.nativeExport, out InteropFunction? function))
                throw new ArgumentException("A managed member references a missing native export.", nameof(model));
            if (function.isVariadic) throw new CSharpPlanningException(function.id, function.source, $"Native export '{function.exportName}' requires a fixed-signature vararg bridge.");
            if (!function.hasPrototype) throw new CSharpPlanningException(function.id, function.source, $"Native export '{function.exportName}' has an unspecified argument list and requires a declared fixed prototype.");
            ArgumentException.ThrowIfNullOrWhiteSpace(function.exportName);
            if (member.parameterNames.Length != function.parameters.Length)
                throw new ArgumentException($"Managed parameter count for '{member.name}' differs from its ABI signature.");
            ImmutableArray<string> parameterNames = member.parameterNames.Select(CSharpNames.Identifier).ToImmutableArray();
            if (parameterNames.Distinct(StringComparer.Ordinal).Count() != parameterNames.Length)
                throw new ArgumentException($"Managed parameter names for '{member.name}' are duplicated.");
            if (member.stringInputs.Distinct().Count() != member.stringInputs.Length)
                throw new ArgumentException("A managed string projection is registered more than once for one parameter.");
            if (member.bufferInputs.Distinct().Count() != member.bufferInputs.Length)
                throw new ArgumentException("A managed buffer projection is registered more than once for one parameter.");
            if (member.valuePointers.IsDefault || member.valuePointers.Distinct().Count() != member.valuePointers.Length)
                throw new ArgumentException("Managed value-pointer projections must be initialized and distinct.");
            foreach (StableId input in member.stringInputs)
            {
                InteropParameter parameter = function.parameters.Single(parameter => parameter.id == input);
                string? restriction = new FriendlyApiEligibilityAnalyzer().GetTextInputRestriction(model.interop, function, parameter);
                if (restriction is not null) throw new CSharpPlanningException(parameter.id, parameter.source ?? function.source, $"Managed string projection for '{parameter.name}' is unsafe: {restriction}");
            }
            foreach (StableId input in member.bufferInputs)
            {
                InteropParameter parameter = function.parameters.Single(parameter => parameter.id == input);
                string? restriction = new BufferProtocolAnalyzer().GetRestriction(model.interop, function, parameter);
                if (restriction is not null) throw new CSharpPlanningException(parameter.id, parameter.source ?? function.source, $"Managed buffer projection for '{parameter.name}' is unsafe: {restriction}");
            }
            ImmutableArray<CSharpBufferProtocolPlan> bufferProtocols = function.parameters.Where(parameter => member.bufferInputs.Contains(parameter.id))
                .Select(parameter => new BufferProtocolPlanner().Plan(model.interop, function, parameter, Resolve)).ToImmutableArray();
            ImmutableArray<CSharpValueParameterPlan>.Builder valueParameters = ImmutableArray.CreateBuilder<CSharpValueParameterPlan>();
            foreach (StableId identity in member.valuePointers)
            {
                InteropParameter parameter = function.parameters.Single(parameter => parameter.id == identity);
                try { valueParameters.Add(new ValueParameterPlanner().Plan(model.interop, function, parameter, Resolve, valueStorage)); }
                catch (NotSupportedException failure)
                { valueOmissions.Add(new(function.id, parameter.id, parameter.source ?? function.source, failure.Message)); }
            }
            try { aggregates.RequireSignature(NativeCallSignature.FromFunction(function)); }
            catch (NotSupportedException failure) { throw new CSharpPlanningException(function.id, function.source, failure.Message, failure); }
            CheckCallableValue(function.returnType, true);
            foreach (InteropParameter parameter in function.parameters) CheckCallableValue(parameter.type, false);
            ImmutableDictionary<int, CSharpRecipeProjectionPlan>.Builder valueProjections = ImmutableDictionary.CreateBuilder<int, CSharpRecipeProjectionPlan>();
            foreach (var conversion in member.valueProjections)
            {
                int parameterIndex = Array.FindIndex(function.parameters.ToArray(), parameter => parameter.id == conversion.nativeParameter);
                if (parameterIndex < 0 || function.error?.errorParameter == conversion.nativeParameter) throw new InvalidOperationException("A managed recipe conversion has no eligible exact ABI value parameter.");
                if (member.stringInputs.Contains(conversion.nativeParameter) || member.bufferInputs.Contains(conversion.nativeParameter) || member.valuePointers.Contains(conversion.nativeParameter))
                    throw new InvalidOperationException("An explicitly managed value projection conflicts with a text or buffer lifetime conversion.");
                string carrier = Resolve(function.parameters[parameterIndex].type);
                if (function.error?.valueParameter == conversion.nativeParameter)
                {
                    if (!carrier.EndsWith('*')) throw new InvalidOperationException("A managed recipe original-result channel is not pointer storage.");
                    carrier = carrier[..^1].TrimEnd();
                }
                CSharpRecipeProjectionPlan projection = projectionValidator.Plan(conversion.projection, carrier, model.interop.pointerSize);
                recipeProjections[projection.id] = projection;
                valueProjections.Add(parameterIndex, projection);
            }
            CSharpRecipeProjectionPlan? returnProjection = member.returnProjection is null ? null
                : projectionValidator.Plan(member.returnProjection, Resolve(function.returnType), model.interop.pointerSize);
            if (returnProjection is not null)
            {
                if (function.error is not null || Resolve(function.returnType) == "void") throw new InvalidOperationException("A direct managed return projection cannot convert a status code or void value.");
                recipeProjections[returnProjection.id] = returnProjection;
            }
            methods.Add(new(function.id, methodName, function.exportName, Resolve(function.returnType),
                function.parameters.Select(parameter => Resolve(parameter.type)).ToImmutableArray(), parameterNames, Convention(function.callingConvention))
            {
                documentation = member.documentationOverride ?? function.documentation,
                documentationContent = member.documentationOverride is null ? DocumentationPlanner.Plan(function) : null,
                stringInputs = function.parameters.Select((
                    parameter,
                    index
                ) => (Parameter: parameter, Index: index)).Where(item => member.stringInputs.Contains(item.Parameter.id))
                    .Select(item => new CSharpStringInputPlan(item.Index, item.Parameter.text!.encoding, item.Parameter.text.byteOrder, item.Parameter.text.allowsNull!.Value,
                        new FriendlyApiEligibilityAnalyzer().GetTextInputAlignment(model.interop, item.Parameter))
                    {
                        lengthIndex = item.Parameter.text.lengthParameter is StableId length ? Array.FindIndex(function.parameters.ToArray(), parameter => parameter.id == length) : null,
                        lengthUnit = item.Parameter.text.lengthUnit,
                        codeUnitWidth = item.Parameter.text.codeUnitWidth,
                        maximumCodeUnits = item.Parameter.text.maximumCodeUnits
                    }).ToImmutableArray(),
                copiedResultIndex = valueParameters.Where(value => value.allocationMode == CSharpValueAllocationMode.NativeValueCopy && value.direction == NativeParameterDirection.Output)
                    .Select(value => (int?)value.parameterIndex).SingleOrDefault(),
                valueProjections = valueProjections.ToImmutable(),
                returnProjection = returnProjection,
                bufferProtocols = bufferProtocols,
                valueParameters = valueParameters.ToImmutable(),
                bufferInputs = bufferProtocols.Select(buffer => new CSharpBufferInputPlan(buffer.parameterIndex, buffer.boundIndex, buffer.elementType,
                    buffer.lengthUnit, buffer.direction, buffer.allowsNull, buffer.alignment)).ToImmutableArray()
            });
        }
        if (methods.Select(method => method.exportName).Distinct(StringComparer.Ordinal).Count() != methods.Count)
            throw new ArgumentException("Native export names are duplicated.");
        bool platformDefaultImports = request.importMode == BindingImportMode.LibraryImport
            && request.managedRuntime?.libraryImportConvention == BGCS.Core.Targets.ManagedImportConventionStrategy.RuntimeDefault;
        if (platformDefaultImports && methods.Any(method => method.callingConvention != runtime.defaultCallingConvention))
            throw new NotSupportedException("A native import differs from the explicitly established runtime default convention.");
        CSharpEmissionPlan result = new(model, request.importMode, types.Values.OrderBy(type => type.nativeType.value, StringComparer.Ordinal).ToImmutableArray(), methods.ToImmutable())
        {
            storage = storage.ToImmutable(),
            recipeProjections = recipeProjections.Values.OrderBy(projection => projection.id, StringComparer.Ordinal).ToImmutableArray(),
            recipeAliases = recipeAliases.ToImmutable(),
            managedRuntime = request.managedRuntime,
            runtimeNamespace = request.runtimeNamespace,
            staticExportTable = request.staticExportTable,
            managedEnums = managedEnums,
            constants = new CSharpConstantPlanner().Plan(model, cancellationToken),
            stringConstants = new CSharpStringConstantPlanner().Plan(model, cancellationToken),
            libraryImportUsesPlatformDefault = platformDefaultImports,
            aggregateCalls = aggregates.calls,
            opaqueCallables = opaqueCallables.Values.OrderBy(callable => callable.nativeType.value, StringComparer.Ordinal).ToImmutableArray(),
            valueParameterOmissions = valueOmissions.ToImmutable()
        };
        result = PhysicalImportPlanner.Apply(result);
        result = ExternalProjectionPlanner.Apply(result);
        result = StorageCustomizationPlanner.Apply(result);
        result = ConstantPresentationPlanner.Apply(result, request, cancellationToken);
        result = DocumentationPlaceholderPlanner.Apply(result);
        result = result with { statusMethods = StatusMethodPlanner.Plan(result) };
        result = ValueStringResultPlanner.Apply(result, request);
        result = OwnedObjectPlanner.Plan(result);
        result = DirectOwnedFactoryPlanner.Apply(result);
        result = ModuleBorrowedFactoryPlanner.Apply(result);
        result = StringResultPlanner.Apply(result);
        result = DirectReceiverObjectPlanner.Apply(result);
        result = ValueStringReceiverPlanner.Apply(result);
        result = ValueReceiverPlanner.Apply(result);
        result = result with { abiDelegates = model.emitCallbackDelegates ? AbiDelegateDeclarationPlanner.Plan(result) : [] };
        var virtuals = VirtualProxyPlanner.Plan(result);
        result = result with { virtualProxies = virtuals.Plans, virtualProxyOmissions = virtuals.Omissions };
        var callbacks = CallScopedCallbackPlanner.Plan(result);
        result = result with { callScopedCallbacks = callbacks.Plans, callbackOmissions = callbacks.Omissions };
        var retainedCallbacks = RetainedCallbackPlanner.Plan(result);
        result = result with { retainedCallbacks = retainedCallbacks.Plans, callbackOmissions = result.callbackOmissions.AddRange(retainedCallbacks.Omissions) };
        var asyncOperations = AsyncOperationPlanner.Plan(result);
        result = result with { asyncOperations = asyncOperations.Plans, callbackOmissions = result.callbackOmissions.AddRange(asyncOperations.Omissions) };
        result = PointerCarrierPlanner.Apply(result);
        result = NullDefaultMethodPlanner.Apply(result);
        result = result with { aliases = AliasPlanner.Plan(result, request) };
        result = result with { functionContainers = FunctionContainerPlanner.Plan(result, request) };
        result = DefaultArgumentPlanner.Apply(result, request);
        result = InputVariationPlanner.Apply(result, request);
        if (model.style == ManagedApiStyle.Static) result = result with { sharedContext = SharedContextPlanner.Plan(result, request) };
        OverloadPlanner.Validate(result, request);
        _ = NativeMetadataPlanner.Plan(result);
        result = ImportLibraryNamePlanner.Apply(result, request, cancellationToken);
        result = result with { capabilities = CapabilityReportPlanner.Plan(result, request) };
        return result;

        string Resolve(StableId id)
        {
            if (types.TryGetValue(id, out CSharpTypePlan? existing)) return existing.syntax;
            InteropType native = model.interop.GetType(id);
            if (!active.Add(id)) throw new NotSupportedException($"Native alias or callback '{native.nativeName}' is recursively unrepresentable.");
            try
            {
                string syntax;
                string? declared = null;
                if (managed[id].frameworkType is string framework)
                {
                    VerifyScalar(native, framework);
                    syntax = Scalar(framework);
                }
                else switch (native.category)
                    {
                        case NativeTypeCategory.Alias:
                        case NativeTypeCategory.Qualified:
                            syntax = Resolve(native.elementType ?? throw new NotSupportedException($"Native type '{native.nativeName}' has no underlying type."));
                            break;
                        case NativeTypeCategory.Pointer:
                            if (native.size != model.interop.pointerSize || native.alignment != model.interop.pointerSize)
                                throw new NotSupportedException($"Pointer '{native.nativeName}' has no established process-sized native storage representation: native size={native.size?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, native alignment={native.alignment?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, required pointer size/alignment={model.interop.pointerSize.ToString(CultureInfo.InvariantCulture)}.");
                            syntax = native.elementType is not StableId element ? "void*" : Resolve(element) + (Underlying(element).category == NativeTypeCategory.Function ? "" : "*");
                            break;
                        case NativeTypeCategory.Function:
                            try
                            {
                                if (native.isVariadic || !native.hasPrototype || native.returnType is not StableId returnType)
                                    throw new NotSupportedException("A managed callback invocation requires a fixed, complete supported prototype; use a native adapter for this signature.");
                                aggregates.RequireSignature(NativeCallSignature.FromCallback(native));
                                CheckCallableValue(returnType, true);
                                foreach (StableId parameter in native.parameterTypes) CheckCallableValue(parameter, false);
                                syntax = "delegate* unmanaged[" + Convention(native.callingConvention ?? "Unknown") + "]<"
                                    + string.Join(", ", native.parameterTypes.Select(Resolve).Append(Resolve(returnType))) + ">";
                            }
                            catch (NotSupportedException unsupported)
                            {
                                // A callback address can be stored/passed without manufacturing an invocation signature.
                                syntax = "nint";
                                opaqueCallables[native.id] = new(native.id, unsupported.Message);
                            }
                            break;
                        case NativeTypeCategory.Record:
                        case NativeTypeCategory.Enumeration:
                        case NativeTypeCategory.Primitive when native.size is > 0:
                            syntax = declared = CSharpNames.Identifier(managed[id].name);
                            break;
                        case NativeTypeCategory.Array:
                            if (native.arrayLength is null)
                                syntax = Resolve(native.elementType ?? throw new NotSupportedException("An incomplete array has no element type."));
                            else
                                syntax = declared = managed[id].isNameExplicit ? CSharpNames.Identifier(managed[id].name)
                                    : "NativeArray_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id.value)))[..16];
                            break;
                        default:
                            throw new NotSupportedException($"Native type '{native.nativeName}' cannot be emitted as a C ABI carrier.");
                    }
                if (declared is not null && !names.Add(declared.TrimStart('@')))
                    throw new ArgumentException($"Managed storage name '{declared}' conflicts with another declaration.");
                if (declared is not null) syntax = CSharpGeneratedTypeScope.DeclarationReference(model, declared);
                types.Add(id, new(id, syntax, declared));
                return syntax;
            }
            catch (NotSupportedException failure) when (failure is not CSharpPlanningException)
            { throw new CSharpPlanningException(native.id, native.source, failure.Message, failure); }
            finally { active.Remove(id); }
        }

        InteropType Underlying(StableId id)
        {
            HashSet<StableId> seen = [];
            InteropType type = model.interop.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!seen.Add(type.id) || type.elementType is not StableId element) throw new NotSupportedException("An alias has an unknown or cyclic underlying type.");
                type = model.interop.GetType(element);
            }
            return type;
        }

        void CheckCallableValue(
            StableId id,
            bool isReturn
        ) {
            InteropType type = Underlying(id);
            try { CheckCallableValueCore(type, isReturn); }
            catch (NotSupportedException failure) when (failure is not CSharpPlanningException)
            { throw new CSharpPlanningException(type.id, type.source, failure.Message, failure); }
        }

        void CheckCallableValueCore(
            InteropType type,
            bool isReturn
        ) {
            if (type.category is NativeTypeCategory.Pointer or NativeTypeCategory.Function) return;
            if (type.category == NativeTypeCategory.Enumeration)
            {
                if (type.elementType is not StableId element) throw new NotSupportedException("An enum underlying ABI is unknown.");
                CheckCallableValue(element, false);
                return;
            }
            if (type.category == NativeTypeCategory.Primitive && managed[type.id].frameworkType is string framework)
            {
                if (framework != "System.Void" || isReturn) return;
                throw new NotSupportedException("Void cannot be used as a parameter carrier.");
            }
            if (type.category == NativeTypeCategory.Record)
            {
                aggregates.CheckCall(type.id);
                return;
            }
            throw new NotSupportedException($"By-value use of '{type.nativeName}' requires independently verified managed calling-ABI evidence or a pointer bridge.");
        }

        CSharpStoragePlan PlanStorage(
            InteropType native,
            string name
        ) {
            ImmutableDictionary<StableId, string> memberNames = ImmutableDictionary<StableId, string>.Empty;
            if (managed.TryGetValue(native.id, out ManagedType? presentation))
            {
                if (presentation.memberNames.IsDefault) throw new ArgumentException("Managed member name mappings must be initialized.");
                memberNames = presentation.memberNames.ToImmutableDictionary(member => member.nativeDeclaration, member => member.name);
            }
            HashSet<StableId> memberIds = native.fields.Select(field => field.id).Concat(native.enumValues.Select(value => value.id)).ToHashSet();
            if (memberNames.Keys.Any(id => !memberIds.Contains(id)))
                throw new ArgumentException("A managed member name mapping references an absent native field or enumerator.");
            string MemberName(
                StableId id,
                string fallback
            ) => memberNames.TryGetValue(id, out string? mapped) ? mapped : fallback;
            if (native.category == NativeTypeCategory.Enumeration)
            {
                if (native.elementType is not StableId element) throw new NotSupportedException("An enum underlying ABI is unknown.");
                string underlying = Resolve(element);
                if (underlying is not ("byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong"))
                    throw new NotSupportedException("An enum requires a supported integral underlying carrier.");
                HashSet<string> members = new(StringComparer.Ordinal) { name };
                ImmutableArray<CSharpEnumValuePlan> values = native.enumValues.Select(value =>
                {
                    string member = CSharpNames.Identifier(MemberName(value.id, value.name));
                    if (!members.Add(member)) throw new ArgumentException($"Enum member '{member}' conflicts with another declaration.");
                    if (!BigInteger.TryParse(value.evaluatedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger number))
                        throw new NotSupportedException($"Enum member '{value.name}' has no independently evaluated integral value.");
                    int bits = checked((int)(Underlying(element).size!.Value * 8));
                    bool signed = underlying is "sbyte" or "short" or "int" or "long";
                    BigInteger minimum = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
                    BigInteger maximum = (BigInteger.One << (signed ? bits - 1 : bits)) - 1;
                    if (number < minimum || number > maximum) throw new NotSupportedException($"Enum member '{value.name}' exceeds its underlying carrier.");
                    return new CSharpEnumValuePlan(member, number.ToString(CultureInfo.InvariantCulture) + (underlying == "ulong" ? "UL" : underlying == "long" ? "L" : underlying == "uint" ? "U" : ""))
                    { documentation = value.documentation };
                }).ToImmutableArray();
                return new(native.id, name, checked((int?)native.size), native.alignment, []) { enumUnderlyingType = underlying, enumValues = values };
            }
            if (native.category == NativeTypeCategory.Record && native.size is null) return new(native.id, name, null, null, []);
            if (native.size is not > 0 || native.size > int.MaxValue || native.alignment is not > 0 || (native.alignment.Value & (native.alignment.Value - 1)) != 0)
                throw new NotSupportedException($"Storage shape of '{native.nativeName}' is incomplete or too large.");
            int size = checked((int)native.size.Value);
            if (native.category == NativeTypeCategory.Array)
            {
                if (native.arrayLength is not > 0 || native.arrayLength > int.MaxValue || native.elementType is not StableId element)
                    throw new NotSupportedException($"Array '{native.nativeName}' requires a positive fixed length and element carrier.");
                InteropType elementType = Underlying(element);
                if (elementType.category is NativeTypeCategory.Pointer or NativeTypeCategory.Function)
                {
                    if (elementType.size != model.interop.pointerSize || native.size != checked(native.arrayLength.Value * model.interop.pointerSize)
                        || native.alignment != model.interop.pointerSize)
                        throw new NotSupportedException("A pointer array requires proven pointer-sized slots and allocation alignment.");
                    return new(native.id, name, size, native.alignment, [])
                    { arrayLength = checked((int)native.arrayLength.Value), arrayElementType = Resolve(element), pointerArrayWidth = model.interop.pointerSize };
                }
                if (elementType.size is not > 0 || checked(elementType.size.Value * native.arrayLength.Value) != size)
                    throw new NotSupportedException("The native array stride differs from its element storage size.");
                if (elementType.alignment > 8) throw new NotSupportedException("Over-aligned array elements require an explicit native storage adapter.");
                return new(native.id, name, size, native.alignment, []) { arrayLength = checked((int)native.arrayLength.Value), arrayElementType = Resolve(element) };
            }
            if (native.category == NativeTypeCategory.Primitive) return new(native.id, name, size, native.alignment, []);
            HashSet<string> fieldNames = new(StringComparer.Ordinal) { name, "m_bitStorage" };
            ImmutableArray<CSharpFlexibleArrayPlan>.Builder flexibleArrays = ImmutableArray.CreateBuilder<CSharpFlexibleArrayPlan>();
            ImmutableArray<CSharpBitfieldPlan>.Builder bitfields = ImmutableArray.CreateBuilder<CSharpBitfieldPlan>();
            ImmutableArray<CSharpFieldPlan>.Builder fields = ImmutableArray.CreateBuilder<CSharpFieldPlan>();
            for (int index = 0; index < native.fields.Length; index++)
            {
                InteropField field = native.fields[index];
                if (field.bitWidth is int width)
                {
                    if (width == 0 && string.IsNullOrEmpty(field.name)) continue;
                    if (model.interop.byteOrder != NativeByteOrder.LittleEndian)
                        throw new NotSupportedException("Native bitfield access requires a proven little-endian target; unknown or big-endian layouts require a native adapter.");
                    InteropType integral = Underlying(field.type);
                    if (integral.category == NativeTypeCategory.Enumeration && integral.elementType is StableId underlying) integral = Underlying(underlying);
                    bool boolean = integral.primitiveKind == "Bool" && integral.size == 1;
                    if (integral.category != NativeTypeCategory.Primitive || integral.size is not (1 or 2 or 4 or 8) || !boolean && integral.isSigned is null
                        || integral.primitiveKind is "Float" or "Double" or "LongDouble" || width < 0 || width > integral.size * 8
                        || field.bitOffset is not long bitOffset || bitOffset < 0 || bitOffset > (long)size * 8 || width > (long)size * 8 - bitOffset)
                        throw new NotSupportedException($"Bitfield '{field.name}' has an unsupported integral shape, width or native bit position.");
                    if (string.IsNullOrEmpty(field.name)) continue;
                    if (width == 0) throw new NotSupportedException("A named native bitfield must have a positive width.");
                    string bitName = CSharpNames.Identifier(MemberName(field.id, field.name));
                    if (!fieldNames.Add(bitName)) throw new ArgumentException($"Bitfield property '{bitName}' conflicts with another member.");
                    bitfields.Add(new(bitName, boolean ? "bool" : Resolve(field.type), bitOffset, width, !boolean && integral.isSigned!.Value) { documentation = field.documentation });
                    continue;
                }
                if (field.offset is null || field.offset < 0 || field.offset > int.MaxValue) throw new NotSupportedException($"Field '{field.name}' has no supported byte offset.");
                InteropType fieldType = Underlying(field.type);
                if (fieldType.category == NativeTypeCategory.Array && fieldType.arrayLength is null)
                {
                    if (index != native.fields.Length - 1 || native.recordKind == "Union" || fieldType.elementType is not StableId element || field.offset > size)
                        throw new NotSupportedException("An incomplete array must be a final native struct tail with a known offset and element.");
                    string tailName = MemberName(field.id, field.name).TrimStart('@');
                    if (tailName.Length == 0) tailName = "Tail";
                    string accessor = CSharpNames.Identifier("Get" + char.ToUpperInvariant(tailName[0]) + tailName[1..] + "Pointer");
                    if (!fieldNames.Add(accessor)) throw new ArgumentException($"Flexible array accessor '{accessor}' conflicts with another member.");
                    flexibleArrays.Add(new(accessor, Resolve(element), checked((int)field.offset.Value)) { documentation = field.documentation });
                    continue;
                }
                if (fieldType.size is not > 0 || fieldType.size > size - field.offset.Value)
                    throw new NotSupportedException($"Field '{field.name}' exceeds its native record or has unknown size.");
                string fieldName = CSharpNames.Identifier(MemberName(field.id, string.IsNullOrWhiteSpace(field.name) ? "AnonymousField" + index : field.name));
                if (!fieldNames.Add(fieldName)) throw new ArgumentException($"Storage field name '{fieldName}' conflicts with another member.");
                fields.Add(new CSharpFieldPlan(fieldName, Resolve(field.type), checked((int)field.offset.Value)) { nativeField = field.id, documentation = field.documentation });
            }
            (int packing, int? managedAlignment) = aggregates.Storage(native.id);
            return new(native.id, name, size, native.alignment, fields.ToImmutable())
            {
                flexibleArrays = flexibleArrays.ToImmutable(),
                bitfields = bitfields.ToImmutable(),
                packing = packing,
                managedAlignment = managedAlignment,
                alignmentProbeName = managedAlignment is null ? null : "__BgcsAlignment_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(native.id.value)))[..16]
            };
        }
    }

    private static string Scalar(string identity) => identity switch
    {
        "System.Void" => "void",
        "System.SByte" => "sbyte",
        "System.Byte" => "byte",
        "System.Int16" => "short",
        "System.UInt16" => "ushort",
        "System.Int32" => "int",
        "System.UInt32" => "uint",
        "System.Int64" => "long",
        "System.UInt64" => "ulong",
        "System.Single" => "float",
        "System.Double" => "double",
        _ => throw new NotSupportedException($"Framework carrier '{identity}' requires explicit backend support.")
    };

    private static void VerifyScalar(
        InteropType native,
        string identity
    ) {
        bool valid = native.category == NativeTypeCategory.Primitive && identity switch
        {
            "System.Void" => native.primitiveKind == "Void" && native.size == 0,
            "System.Single" => native.primitiveKind == "Float" && native.size == 4,
            "System.Double" => native.primitiveKind == "Double" && native.size == 8,
            "System.Byte" when native.primitiveKind == "Bool" => native.size == 1,
            "System.SByte" => native.size == 1 && native.isSigned == true,
            "System.Byte" => native.size == 1 && native.isSigned == false,
            "System.Int16" => native.size == 2 && native.isSigned == true,
            "System.UInt16" => native.size == 2 && native.isSigned == false,
            "System.Int32" => native.size == 4 && native.isSigned == true,
            "System.UInt32" => native.size == 4 && native.isSigned == false,
            "System.Int64" => native.size == 8 && native.isSigned == true,
            "System.UInt64" => native.size == 8 && native.isSigned == false,
            _ => false
        };
        if (!valid || native.primitiveKind is "Float16" or "BFloat16" or "Half" or "LongDouble")
            throw new NotSupportedException($"Managed scalar '{identity}' is not proven compatible with native type '{native.nativeName}'.");
    }

    private static string Convention(string native) => native switch
    {
        "C" or "Cdecl" or "Default" => "Cdecl",
        "X86StdCall" or "StdCall" or "Stdcall" => "Stdcall",
        "X86FastCall" or "FastCall" or "Fastcall" => "Fastcall",
        "X86ThisCall" or "ThisCall" or "Thiscall" => "Thiscall",
        _ => throw new NotSupportedException($"Calling convention '{native}' requires explicit backend ABI support.")
    };
}
