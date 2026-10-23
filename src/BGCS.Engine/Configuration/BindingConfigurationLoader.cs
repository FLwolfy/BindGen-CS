using System;
using BGCS.Core.Hosting;
using BGCS.Core.Interop;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Bridge;
using BGCS.Core.Managed;

namespace BGCS.Configuration;

/// <summary>Reads only the strict grouped BGCS 3.0 format and freezes its declared defaults.</summary>
public sealed class BindingConfigurationLoader
{
    private readonly IHostPathService m_paths;
    /// <summary>Uses the host selected by the caller before any configuration files are read.</summary>
    /// <param name="paths">The explicit host path service.</param>
    public BindingConfigurationLoader(IHostPathService paths) => m_paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private static readonly JsonSerializerOptions s_options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        MaxDepth = 64,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    internal static JsonSerializerOptions CreateSerializerOptions() => new(s_options);

    /// <summary>Reads a local root and its explicit local bases without old-format fallback.</summary>
    /// <param name="path">The native configuration path.</param>
    /// <returns>The validated immutable grouped configuration.</returns>
    /// <exception cref="JsonException">The format, enum identity or field is unknown.</exception>
    /// <exception cref="ArgumentException">A required choice or execution limit is invalid.</exception>
    public BindingConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadWithInputs(path).configuration;
    }

    /// <summary>Composes local bases and a named profile with exact document provenance.</summary>
    /// <param name="path">The local root configuration.</param>
    /// <param name="profile">An explicit preset selection overriding the document selector.</param>
    /// <param name="cancellationToken">Cancels local file reads and composition.</param>
    /// <returns>The effective configuration and all input content identities.</returns>
    public ConfigurationLoadResult LoadWithInputs(
        string path,
        string? profile = null,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadWithInputsAsync(path, profile, cancellationToken).GetAwaiter().GetResult();
    }

    /// <summary>Asynchronously composes declared configuration inputs with bounded local reads and immutable content identities.</summary>
    /// <param name="path">The local root configuration.</param>
    /// <param name="profile">An explicit preset selection overriding the document selector.</param>
    /// <param name="cancellationToken">Cancels local file reads and composition.</param>
    /// <returns>The validated configuration, local input closure.</returns>
    public Task<ConfigurationLoadResult> LoadWithInputsAsync(
        string path,
        string? profile = null,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ConfigurationComposer(m_paths).LoadAsync(path, profile, cancellationToken);
    }

    /// <summary>Applies the same configuration invariants to SDK-created immutable values.</summary>
    /// <param name="configuration">The caller's configuration snapshot.</param>
    /// <returns>The configuration with explicit effective language defaults.</returns>
    public static BindingConfiguration ValidateAndFreeze(BindingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.schemaVersion != 2) throw new ArgumentException("Only BGCS configuration schemaVersion 2 is supported.");
        ArgumentNullException.ThrowIfNull(configuration.profiles);
        if (!configuration.extends.IsDefaultOrEmpty || configuration.profile is not null || configuration.profiles.Count != 0)
            throw new ArgumentException("Configuration inheritance and named profiles must be composed through FromFile or the configuration input loader before SDK execution.");
        ArgumentNullException.ThrowIfNull(configuration.library);
        ArgumentNullException.ThrowIfNull(configuration.source);
        ArgumentNullException.ThrowIfNull(configuration.source.selection);
        BGCS.Analysis.Declarations.DeclarationSelector.ValidateCategories(configuration.source.selection);
        if (configuration.source.includeHeaderHelpers && configuration.source.language != BGCS.Core.Inputs.SourceLanguage.C)
            throw new ArgumentException("source.includeHeaderHelpers applies to C headers only; C++ callables use the C++ bridge.");
        new BGCS.NativeBridge.Planning.NativeVariadicAdapterPlanner().ValidateRequests(configuration.source.variadicVariants);
        foreach (ImmutableArray<string> choices in new[] { configuration.source.selection.includeHeaders, configuration.source.selection.excludeHeaders,
            configuration.source.selection.includeDeclarations, configuration.source.selection.excludeDeclarations })
        {
            if (choices.IsDefault) throw new ArgumentException("Source selection lists must be initialized immutable arrays.");
            HashSet<string> unique = new(StringComparer.Ordinal);
            foreach (string choice in choices)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(choice);
                if (!unique.Add(choice)) throw new ArgumentException("A source selection list contains a duplicate exact selector.");
            }
        }
        if (configuration.source.selection.includeHeaders.Intersect(configuration.source.selection.excludeHeaders, StringComparer.Ordinal).Any()
            || configuration.source.selection.includeDeclarations.Intersect(configuration.source.selection.excludeDeclarations, StringComparer.Ordinal).Any())
            throw new ArgumentException("One exact source selection cannot be both included and excluded.");
        ArgumentNullException.ThrowIfNull(configuration.api);
        ManagedPresentationConfigurationValidator.Validate(configuration.api);
        if (configuration.api.usings.IsDefault) throw new ArgumentException("Additional namespaces must be initialized.");
        foreach (string ns in configuration.api.usings) ArgumentException.ThrowIfNullOrWhiteSpace(ns);
        BGCS.CSharp.Projection.ManagedNamingResolver.Validate(configuration.api.naming);
        if (configuration.api.defaultOverloadLimit is < 1 or > 4096) throw new ArgumentException("api.defaultOverloadLimit must be between 1 and 4096.");
        if (configuration.api.functions.IsDefault) throw new ArgumentException("Managed function rules must be initialized.");
        if (configuration.api.records.IsDefault) throw new ArgumentException("Record presentation rules must be initialized.");
        foreach (ManagedRecordRuleRequest record in configuration.api.records)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentException.ThrowIfNullOrWhiteSpace(record.nativeType);
            if (record.fields.IsDefault || record.constructorFields.IsDefault) throw new ArgumentException("Record field choices must be initialized.");
            if (record.name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(record.name);
            foreach (ManagedFieldRuleRequest field in record.fields)
            {
                ArgumentNullException.ThrowIfNull(field);
                ArgumentException.ThrowIfNullOrWhiteSpace(field.field);
                if (field.name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(field.name);
            }
            foreach (string field in record.constructorFields) ArgumentException.ThrowIfNullOrWhiteSpace(field);
            if (record.validity is { } validity)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(validity.field);
                ArgumentException.ThrowIfNullOrWhiteSpace(validity.propertyName);
                ArgumentNullException.ThrowIfNull(validity.invalidValue);
                if (!Enum.IsDefined(validity.invalidValue.kind)) throw new ArgumentException("A record validity sentinel kind is undefined.");
            }
        }
        HashSet<string> functionRules = new(StringComparer.Ordinal);
        foreach (ManagedFunctionRuleRequest function in configuration.api.functions)
        {
            ArgumentNullException.ThrowIfNull(function);
            ArgumentException.ThrowIfNullOrWhiteSpace(function.function);
            if (!functionRules.Add(function.function)) throw new ArgumentException("A function selector has multiple presentation rules.");
            if (function.name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(function.name);
            if (function.defaults.IsDefault) throw new ArgumentException("Default argument choices must be initialized.");
            if (function.parameters.IsDefault) throw new ArgumentException("Friendly parameter conversion choices must be initialized.");
            if (function.inputVariations.IsDefault) throw new ArgumentException("Alternate input choices must be initialized.");
            foreach (ManagedInputVariationRequest variation in function.inputVariations)
            {
                ArgumentNullException.ThrowIfNull(variation);
                if (variation.name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(variation.name);
                if (variation.parameters.IsDefaultOrEmpty) throw new ArgumentException("An input variation requires an explicit parameter conversion.");
                HashSet<string> inputs = new(StringComparer.Ordinal);
                foreach (ManagedParameterProjectionRequest parameter in variation.parameters)
                {
                    ArgumentNullException.ThrowIfNull(parameter);
                    ArgumentException.ThrowIfNullOrWhiteSpace(parameter.parameter);
                    if (!inputs.Add(parameter.parameter)) throw new ArgumentException("An input variation repeats a parameter selector.");
                    ArgumentNullException.ThrowIfNull(parameter.projection);
                    ArgumentException.ThrowIfNullOrWhiteSpace(parameter.projection.managedType);
                    ArgumentException.ThrowIfNullOrWhiteSpace(parameter.projection.managedToNativeExpression);
                }
            }
            HashSet<string> projectedParameters = new(StringComparer.Ordinal);
            foreach (ManagedParameterProjectionRequest parameter in function.parameters)
            {
                ArgumentNullException.ThrowIfNull(parameter);
                ArgumentException.ThrowIfNullOrWhiteSpace(parameter.parameter);
                if (!projectedParameters.Add(parameter.parameter)) throw new ArgumentException("A parameter selector has multiple friendly conversions.");
                ArgumentNullException.ThrowIfNull(parameter.projection);
                ArgumentException.ThrowIfNullOrWhiteSpace(parameter.projection.managedType);
                ArgumentException.ThrowIfNullOrWhiteSpace(parameter.projection.managedToNativeExpression);
            }
            if (function.returns is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(function.returns.managedType);
                ArgumentException.ThrowIfNullOrWhiteSpace(function.returns.nativeToManagedExpression);
            }
            foreach (ManagedDefaultArgumentRequest argument in function.defaults)
            {
                ArgumentNullException.ThrowIfNull(argument);
                ArgumentException.ThrowIfNullOrWhiteSpace(argument.parameter);
                ArgumentNullException.ThrowIfNull(argument.value);
                if (!Enum.IsDefined(argument.value.kind)) throw new ArgumentException("A default argument kind is undefined.");
            }
        }
        if (configuration.api.externalTypes.IsDefault) throw new ArgumentException("External types must be an initialized immutable collection.");
        foreach (ExternalTypeConfiguration external in configuration.api.externalTypes)
        {
            ArgumentNullException.ThrowIfNull(external);
            ArgumentException.ThrowIfNullOrWhiteSpace(external.nativeType);
            _ = BGCS.CSharp.Planning.ExternalCarrierPlanner.NormalizeType(external.managedType);
            ArgumentNullException.ThrowIfNull(external.fields);
            if (external.fields.IsEmpty) throw new ArgumentException("An external type requires explicit complete field mappings.");
            foreach ((string native, string managed) in external.fields)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(native);
                _ = BGCS.CSharp.Planning.ExternalCarrierPlanner.NormalizeField(managed);
            }
        }
        if (configuration.api.functionAliases.IsDefault) throw new ArgumentException("Function aliases must be an initialized immutable collection.");
        if (configuration.api.functionContainers.IsDefault) throw new ArgumentException("Function containers must be an initialized immutable collection.");
        if (configuration.api.memberFunctions.IsDefault) throw new ArgumentException("C member mappings must be an initialized immutable collection.");
        if (configuration.api.pointerCarriers.IsDefault || configuration.api.pointerFunctions.IsDefault) throw new ArgumentException("Pointer presentation collections must be initialized.");
        foreach (ManagedPointerCarrierRequest carrier in configuration.api.pointerCarriers)
        {
            ArgumentNullException.ThrowIfNull(carrier);
            ArgumentException.ThrowIfNullOrWhiteSpace(carrier.nativeType);
            ValidatePointerName(carrier.name);
            if (carrier.pointerDepth <= 0) throw new ArgumentException("A pointer carrier requires a positive native indirection depth.");
        }
        foreach (ManagedPointerFunctionRequest function in configuration.api.pointerFunctions)
        {
            ArgumentNullException.ThrowIfNull(function);
            ArgumentException.ThrowIfNullOrWhiteSpace(function.function);
            if (function.parameters.IsDefault || function.parameters.Length == 0 && function.returns is null) throw new ArgumentException("A pointer function must select address parameters or a result.");
            if (function.name is not null) ValidatePointerName(function.name);
            if (function.returns is not null) ValidatePointerName(function.returns);
            if (function.receiver is not null) ArgumentException.ThrowIfNullOrWhiteSpace(function.receiver);
            foreach (ManagedPointerParameterRequest parameter in function.parameters)
            {
                ArgumentNullException.ThrowIfNull(parameter);
                ArgumentException.ThrowIfNullOrWhiteSpace(parameter.parameter);
                ValidatePointerName(parameter.carrier);
            }
        }
        foreach (ManagedReceiverRequest member in configuration.api.memberFunctions)
        {
            ArgumentNullException.ThrowIfNull(member);
            ArgumentException.ThrowIfNullOrWhiteSpace(member.function);
            ArgumentException.ThrowIfNullOrWhiteSpace(member.nativeType);
            ArgumentException.ThrowIfNullOrWhiteSpace(member.receiver);
            if (!Enum.IsDefined(member.kind)) throw new ArgumentException("The C member lifetime protocol is unknown.");
            if (member.kind == ManagedReceiverKind.ModuleBorrowed && member.releaseDomain is not null)
                throw new ArgumentException("A module-borrowed object cannot declare a native release domain.");
            if (member.kind == ManagedReceiverKind.Value && (member.objectName is not null || member.releaseDomain is not null))
                throw new ArgumentException("A value receiver uses its existing carrier and cannot declare an object wrapper name or release domain.");
            if (member.objectName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(member.objectName);
            if (member.name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(member.name);
            if (member.releaseDomain is not null) ArgumentException.ThrowIfNullOrWhiteSpace(member.releaseDomain);
        }
        foreach (ManagedFunctionContainerRequest container in configuration.api.functionContainers)
        {
            ArgumentNullException.ThrowIfNull(container);
            ArgumentException.ThrowIfNullOrWhiteSpace(container.name);
            if (container.functions.IsDefaultOrEmpty) throw new ArgumentException("A function container requires explicit selected operations.");
            foreach (ManagedFunctionAliasRequest function in container.functions)
            {
                ArgumentNullException.ThrowIfNull(function);
                ArgumentException.ThrowIfNullOrWhiteSpace(function.function);
                ArgumentException.ThrowIfNullOrWhiteSpace(function.name);
            }
        }
        foreach (ManagedFunctionAliasRequest alias in configuration.api.functionAliases)
        {
            ArgumentNullException.ThrowIfNull(alias);
            ArgumentException.ThrowIfNullOrWhiteSpace(alias.function);
            ArgumentException.ThrowIfNullOrWhiteSpace(alias.name);
        }
        if (configuration.api.runtime is { } runtime) ArgumentException.ThrowIfNullOrWhiteSpace(runtime.value);
        ArgumentNullException.ThrowIfNull(configuration.semantics);
        ArgumentNullException.ThrowIfNull(configuration.bridge);
        if (!Enum.IsDefined(configuration.bridge.aggregateStrategy)) throw new ArgumentException("Unknown bridge.aggregateStrategy.");
        ArgumentNullException.ThrowIfNull(configuration.bridge.cpp);
        ArgumentNullException.ThrowIfNull(configuration.bridge.cpp.templateInstantiations);
        _ = new BGCS.NativeBridge.Planning.TemplateInstantiationPlanner().Plan(configuration.bridge.cpp.templateInstantiations);
        _ = BGCS.Analysis.Bridge.CppFamilyBindingValidator.Freeze(configuration.bridge.cpp.familyBindings);
        if (configuration.bridge.typeLowerings.IsDefault || configuration.bridge.callableLowerings.IsDefault || configuration.bridge.nativeShims.IsDefault || configuration.bridge.cpp.virtualCallbacks.IsDefault)
            throw new ArgumentException("C++ recipe and shim collections must be initialized immutable arrays.");
        HashSet<string> cppIds = new(StringComparer.Ordinal);
        foreach (TypeLoweringRecipe recipe in configuration.bridge.typeLowerings)
        {
            _ = new BGCS.NativeBridge.Recipes.LoweringRecipeCompiler().Compile(recipe);
            if (!cppIds.Add(recipe.id)) throw new ArgumentException("C++ recipe and shim identities must be unique.");
        }
        new BGCS.CSharp.Projection.ManagedRecipeProjectionValidator().ValidateRecipes(configuration.bridge.typeLowerings);
        _ = new BGCS.NativeBridge.Planning.NativeBridgePlanner(callableRecipes: configuration.bridge.callableLowerings);
        foreach (CallableLoweringRecipe recipe in configuration.bridge.callableLowerings)
            if (!cppIds.Add(recipe.id)) throw new ArgumentException("C++ recipe and shim identities must be unique.");
        foreach (NativeShimRequest shim in configuration.bridge.nativeShims)
        {
            _ = new BGCS.NativeBridge.Shims.NativeShimPlanner().Plan(shim, AppContext.BaseDirectory);
            if (!cppIds.Add(shim.id)) throw new ArgumentException("C++ recipe and shim identities must be unique.");
        }
        foreach (VirtualCallbackRequest callback in configuration.bridge.cpp.virtualCallbacks)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentException.ThrowIfNullOrWhiteSpace(callback.id);
            ArgumentException.ThrowIfNullOrWhiteSpace(callback.nativeInterface);
            if (callback.releaseThreadDomain is not ("unknown" or "any"))
                throw new ArgumentException("A virtual callback cleanup thread must be unknown or the explicit any-thread assertion.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(callback.id, "^[A-Za-z][A-Za-z0-9_.-]{0,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                throw new ArgumentException("A virtual callback identity must be a portable stable name.");
            if (!cppIds.Add(callback.id)) throw new ArgumentException("C++ recipe, shim and callback identities must be unique.");
        }
        if (configuration.bridge.cpp.sequenceInputAdapters.IsDefault) throw new ArgumentException("Sequence input adapter requests cannot be an uninitialized collection.");
        HashSet<string> sequenceCallables = new(StringComparer.Ordinal);
        foreach (SequenceInputAdapterRequest adapter in configuration.bridge.cpp.sequenceInputAdapters)
        {
            ArgumentNullException.ThrowIfNull(adapter);
            ArgumentException.ThrowIfNullOrWhiteSpace(adapter.function);
            if (!sequenceCallables.Add(adapter.function)) throw new ArgumentException("One native callable can declare only one sequence input adapter request.");
            if (adapter.parameters.IsDefaultOrEmpty) throw new ArgumentException("A sequence input adapter must select at least one original parameter.");
            HashSet<string> parameters = new(StringComparer.Ordinal);
            foreach (SequenceInputParameterRequest parameter in adapter.parameters)
            {
                ArgumentNullException.ThrowIfNull(parameter);
                ArgumentException.ThrowIfNullOrWhiteSpace(parameter.parameter);
                if (!parameters.Add(parameter.parameter)) throw new ArgumentException("A sequence input adapter selected one original parameter more than once.");
                if (!Enum.IsDefined(parameter.lifetime)) throw new ArgumentException("A sequence input adapter declared an unknown lifetime.");
            }
        }
        if (configuration.source.language != BGCS.Core.Inputs.SourceLanguage.Cpp
            && (!configuration.bridge.cpp.templateInstantiations.types.IsEmpty || !configuration.bridge.cpp.templateInstantiations.functions.IsEmpty
                || !configuration.bridge.cpp.virtualCallbacks.IsEmpty || !configuration.bridge.cpp.sequenceInputAdapters.IsEmpty || !configuration.bridge.cpp.familyBindings.IsEmpty))
            throw new ArgumentException("Explicit C++ template, recipe and shim requests require cpp source language.");
        ArgumentNullException.ThrowIfNull(configuration.output);
        ArgumentNullException.ThrowIfNull(configuration.runtime);
        ArgumentNullException.ThrowIfNull(configuration.cache);
        ArgumentNullException.ThrowIfNull(configuration.execution);
        ArgumentNullException.ThrowIfNull(configuration.api.projectionInputs);
        ArgumentNullException.ThrowIfNull(configuration.extensions);
        if (configuration.extensions.plugins.IsDefault) throw new ArgumentException("Plugin manifest inputs cannot be an uninitialized collection.");
        foreach (string plugin in configuration.extensions.plugins) ArgumentException.ThrowIfNullOrWhiteSpace(plugin);
        ArgumentNullException.ThrowIfNull(configuration.extensions.options);
        foreach ((string id, PluginSettingsConfiguration settings) in configuration.extensions.options)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(settings.valuePathBases);
            if (settings.values.ValueKind != JsonValueKind.Object) throw new ArgumentException("A plugin values namespace must be an object.");
            if (settings.pathBase is not null) ArgumentException.ThrowIfNullOrWhiteSpace(settings.pathBase);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.library.id);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.library.nativeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.api.@namespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.api.name);
        ArgumentNullException.ThrowIfNull(configuration.api.enumMappings);
        _ = new BGCS.CSharp.Planning.ManagedEnumPlanner().Plan(configuration.api.enums);
        HashSet<string> declaredEnums = configuration.api.enums.Select(declaration => declaration.name.TrimStart('@')).ToHashSet(StringComparer.Ordinal);
        foreach ((string nativeName, string managedName) in configuration.api.enumMappings)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nativeName);
            ArgumentException.ThrowIfNullOrWhiteSpace(managedName);
            if (!declaredEnums.Contains(managedName.TrimStart('@'))) throw new ArgumentException("An enum mapping references an undeclared managed enum.");
        }
        if (configuration.source.language is not (BGCS.Core.Inputs.SourceLanguage.C or BGCS.Core.Inputs.SourceLanguage.Cpp))
            throw new ArgumentException("Source language must be c or cpp.");
        if (configuration.source.headers.IsDefaultOrEmpty || configuration.targets.IsDefaultOrEmpty)
            throw new ArgumentException("At least one header and one native target are required.");
        foreach (string header in configuration.source.headers) ArgumentException.ThrowIfNullOrWhiteSpace(header);
        if (configuration.semantics.stringParameters.IsDefault || configuration.semantics.stringResults.IsDefault || configuration.semantics.ownedResults.IsDefault || configuration.semantics.bufferParameters.IsDefault || configuration.semantics.valueParameters.IsDefault || configuration.semantics.callbackParameters.IsDefault)
            throw new ArgumentException("Native semantic rules cannot be uninitialized collections.");
        if (configuration.semantics.borrowedParameters.IsDefault || configuration.semantics.borrowedResults.IsDefault)
            throw new ArgumentException("Borrowed parameter and result rules must be initialized.");
        HashSet<string> borrowedResults = new(StringComparer.Ordinal);
        foreach (BorrowedResultConfiguration borrowed in configuration.semantics.borrowedResults)
        {
            ArgumentNullException.ThrowIfNull(borrowed);
            ArgumentException.ThrowIfNullOrWhiteSpace(borrowed.function);
            if (!borrowedResults.Add(borrowed.function)) throw new ArgumentException("A borrowed result has duplicate lifetime assertions.");
            if (borrowed.lifetime != NativeLifetimeKind.Module)
                throw new ArgumentException("A module-borrowed result requires an explicit Module address lifetime guarantee.");
        }
        HashSet<(string Function, string Parameter)> borrowing = [];
        foreach (BorrowedParameterConfiguration borrowed in configuration.semantics.borrowedParameters)
        {
            ArgumentNullException.ThrowIfNull(borrowed);
            ArgumentException.ThrowIfNullOrWhiteSpace(borrowed.function);
            ArgumentException.ThrowIfNullOrWhiteSpace(borrowed.parameter);
            if (!borrowing.Add((borrowed.function, borrowed.parameter))) throw new ArgumentException("A borrowed parameter has multiple lifetime rules.");
            if (!Enum.IsDefined(borrowed.lifetime)) throw new ArgumentException("A borrowed parameter lifetime is undefined.");
        }
        HashSet<(string Function, string Parameter)> callbackRules = [];
        if (configuration.semantics.asyncOperations.IsDefault) throw new ArgumentException("Async operation rules must be initialized.");
        HashSet<string> asyncRules = new(StringComparer.Ordinal);
        foreach (AsyncOperationConfiguration operation in configuration.semantics.asyncOperations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation.function);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation.completionCallback);
            if (!asyncRules.Add(operation.function)) throw new ArgumentException("An async operation has multiple completion protocols.");
            if (!Enum.IsDefined(operation.completionQuiescence) || !Enum.IsDefined(operation.cancellationQuiescence)
                || operation.resultArgumentIndex < 0 || operation.statusArgumentIndex < 0 || operation.resultArgumentIndex == operation.statusArgumentIndex)
                throw new ArgumentException("An async operation requires valid distinct native result and status channels.");
            if ((operation.cancelFunction is null) != (operation.cancelHandleParameter is null)) throw new ArgumentException("An async cancellation request needs both export and handle input.");
            if (operation.cancelFunction is null && operation.cancellationIsNonBlocking is not null)
                throw new ArgumentException("An async cancellationIsNonBlocking assertion requires an exact cancelFunction and cancelHandleParameter.");
            if (operation.cancelFunction is not null) ArgumentException.ThrowIfNullOrWhiteSpace(operation.cancelFunction);
            if (operation.cancelHandleParameter is not null) ArgumentException.ThrowIfNullOrWhiteSpace(operation.cancelHandleParameter);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation.successStatus);
            if (operation.retainedParameters.IsDefault || operation.retainedParameters.Any(string.IsNullOrWhiteSpace)
                || operation.retainedParameters.Distinct(StringComparer.Ordinal).Count() != operation.retainedParameters.Length)
                throw new ArgumentException("Async retained parameter names must be initialized, unique and nonempty.");
        }
        foreach (CallbackParameterConfiguration callback in configuration.semantics.callbackParameters)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentException.ThrowIfNullOrWhiteSpace(callback.function);
            ArgumentException.ThrowIfNullOrWhiteSpace(callback.parameter);
            if (!callbackRules.Add((callback.function, callback.parameter))) throw new ArgumentException("A native callback contract is declared more than once.");
            if (!Enum.IsDefined(callback.lifetime) || !Enum.IsDefined(callback.threading) || !Enum.IsDefined(callback.unregisterQuiescence))
                throw new ArgumentException("A native callback contract has an unknown lifetime, threading or unregister guarantee.");
            if (callback.callableUserDataIndex is < 0 || (callback.userDataParameter is null) != (callback.callableUserDataIndex is null))
                throw new ArgumentException("A callback user-data protocol requires both an outer parameter and a nonnegative callback argument index.");
            if (callback.userDataParameter is not null) ArgumentException.ThrowIfNullOrWhiteSpace(callback.userDataParameter);
            if (callback.unregisterFunction is not null) ArgumentException.ThrowIfNullOrWhiteSpace(callback.unregisterFunction);
            if (callback.registration is { } registration)
            {
                if (!Enum.IsDefined(registration.kind) || !Enum.IsDefined(registration.nullResultQuiescence) || !Enum.IsDefined(registration.failureResultQuiescence)
                    || registration.releaseThreadDomain is not ("unknown" or "any" or "eventLoop"))
                    throw new ArgumentException("A callback registration has an unknown null-result guarantee or release thread domain.");
                if (registration.kind == NativeCallbackRegistrationKind.ReturnedHandle)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(registration.unregisterHandleParameter);
                    if (registration.unregisterCallbackParameter is not null || registration.unregisterUserDataParameter is not null || registration.successfulReturn is not null
                        || registration.failureResultQuiescence != NativeQuiescenceKind.Unknown)
                        throw new ArgumentException("A returned-handle registration cannot also bind a callback/state unregister protocol.");
                }
                else
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(registration.unregisterCallbackParameter);
                    ArgumentException.ThrowIfNullOrWhiteSpace(registration.unregisterUserDataParameter);
                    if (!string.IsNullOrEmpty(registration.unregisterHandleParameter) || registration.nullResultQuiescence != NativeQuiescenceKind.Unknown)
                        throw new ArgumentException("A callback/state registration cannot also bind a returned native handle.");
                }
                if (callback.unregisterFunction is null) throw new ArgumentException("A callback registration requires its explicit native unregister function.");
            }
        }
        HashSet<(string Function, string Parameter)> bufferRules = [];
        foreach (BufferParameterConfiguration buffer in configuration.semantics.bufferParameters)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentException.ThrowIfNullOrWhiteSpace(buffer.function);
            ArgumentException.ThrowIfNullOrWhiteSpace(buffer.parameter);
            if (buffer.lengthParameter is null && buffer.capacityParameter is null) throw new ArgumentException("A native buffer requires an explicit length or capacity parameter.");
            if (buffer.lengthParameter is not null) ArgumentException.ThrowIfNullOrWhiteSpace(buffer.lengthParameter);
            if (buffer.capacityParameter is not null) ArgumentException.ThrowIfNullOrWhiteSpace(buffer.capacityParameter);
            if (buffer.writtenCountParameter is not null) ArgumentException.ThrowIfNullOrWhiteSpace(buffer.writtenCountParameter);
            if (!bufferRules.Add((buffer.function, buffer.parameter))) throw new ArgumentException("A native buffer contract is declared more than once.");
            if (!Enum.IsDefined(buffer.lengthUnit) || !Enum.IsDefined(buffer.direction) || !Enum.IsDefined(buffer.lifetime)) throw new ArgumentException("A native buffer contract has an unknown choice.");
            if (buffer.requiredAlignment is int alignment && (alignment <= 0 || (alignment & (alignment - 1)) != 0)) throw new ArgumentException("A native buffer alignment must be a positive power of two.");
        }
        HashSet<(string Function, string Parameter)> valueRules = [];
        foreach (ValueParameterConfiguration value in configuration.semantics.valueParameters)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentException.ThrowIfNullOrWhiteSpace(value.function);
            ArgumentException.ThrowIfNullOrWhiteSpace(value.parameter);
            if (!valueRules.Add((value.function, value.parameter))) throw new ArgumentException("A native value-pointer contract is declared more than once.");
            if (!Enum.IsDefined(value.direction) || !Enum.IsDefined(value.lifetime)) throw new ArgumentException("A native value-pointer contract has an unknown choice.");
            if (value.requiredAlignment is int alignment && (alignment <= 0 || (alignment & (alignment - 1)) != 0)) throw new ArgumentException("A native value alignment must be a positive power of two.");
        }
        HashSet<string> ownedRules = new(StringComparer.Ordinal);
        foreach (OwnedResultConfiguration owned in configuration.semantics.ownedResults)
        {
            ArgumentNullException.ThrowIfNull(owned);
            ArgumentException.ThrowIfNullOrWhiteSpace(owned.function);
            if (!ownedRules.Add(owned.function)) throw new ArgumentException("An owned native result contract is declared more than once.");
            if (borrowedResults.Contains(owned.function)) throw new ArgumentException("One native result cannot be declared both owned and module-borrowed.");
            if (owned.releaseFunction is not null) ArgumentException.ThrowIfNullOrWhiteSpace(owned.releaseFunction);
            if (owned.releaseDomain is not null) ArgumentException.ThrowIfNullOrWhiteSpace(owned.releaseDomain);
            if (owned.releaseThreadDomain is not ("any" or "unknown"))
                throw new ArgumentException("Owned releaseThreadDomain must be any or unknown; affine cleanup requires an explicit scheduler contract.");
        }
        HashSet<(string Function, string Parameter)> stringRules = [];
        foreach (StringParameterConfiguration text in configuration.semantics.stringParameters)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentException.ThrowIfNullOrWhiteSpace(text.function);
            ArgumentException.ThrowIfNullOrWhiteSpace(text.parameter);
            if (!stringRules.Add((text.function, text.parameter))) throw new ArgumentException("A native text parameter contract is declared more than once.");
            if (!Enum.IsDefined(text.encoding) || !Enum.IsDefined(text.byteOrder) || !Enum.IsDefined(text.lengthUnit) || !Enum.IsDefined(text.direction) || !Enum.IsDefined(text.lifetime) || !Enum.IsDefined(text.lengthDirection))
                throw new ArgumentException("A native text contract has an unknown choice.");
            if (text.codeUnitWidth < 0) throw new ArgumentException("A native code-unit width cannot be negative.");
            if (text.maximumCodeUnits <= 0) throw new ArgumentException("A native text input code-unit allocation bound must be positive.");
            if (text.lengthParameter is not null) ArgumentException.ThrowIfNullOrWhiteSpace(text.lengthParameter);
            if (text.lengthParameter is null && text.lengthDirection != NativeParameterDirection.Unknown)
                throw new ArgumentException("A native text input length direction requires its exact native length parameter.");
        }
        HashSet<string> stringResults = new(StringComparer.Ordinal);
        foreach (StringResultConfiguration text in configuration.semantics.stringResults)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentException.ThrowIfNullOrWhiteSpace(text.function);
            if (!stringResults.Add(text.function)) throw new ArgumentException("A native text result contract is declared more than once.");
            if (!Enum.IsDefined(text.encoding) || !Enum.IsDefined(text.byteOrder) || !Enum.IsDefined(text.lengthUnit) || !Enum.IsDefined(text.lengthDirection))
                throw new ArgumentException("A native text result contract contains an undefined choice.");
            if (text.codeUnitWidth is <= 0 || text.maximumCodeUnits <= 0 || text.fixedLength is < 0)
                throw new ArgumentException("Native result code-unit widths and scan/copy caps must be positive, and fixed lengths cannot be negative.");
            if (text.lengthParameter is not null) ArgumentException.ThrowIfNullOrWhiteSpace(text.lengthParameter);
            if (text.fixedLength is not null && text.lengthParameter is not null || text.zeroTerminated == true && (text.fixedLength is not null || text.lengthParameter is not null))
                throw new ArgumentException("A native text result selects exactly one zero-terminated, fixed-length or parameter-length extent protocol.");
            if (text.lengthParameter is null && (text.lengthDirection != NativeParameterDirection.Unknown || text.lengthOutputCallBounded is not null))
                throw new ArgumentException("Native result length channel options require an explicit length parameter.");
        }
        HashSet<string> targets = new(StringComparer.Ordinal);
        foreach (TargetConfiguration target in configuration.targets)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(target.settings);
            ArgumentException.ThrowIfNullOrWhiteSpace(target.id);
            _ = new BGCS.Core.Targets.NativeTargetRequest(new(target.id), tripleOverride: target.triple);
            if (!targets.Add(target.id)) throw new ArgumentException($"Target '{target.id}' is configured more than once.");
        }
        if (!Enum.IsDefined(configuration.api.importMode) || !Enum.IsDefined(configuration.api.style)) throw new ArgumentException("A managed API choice is unknown.");
        if (configuration.api.framework != "net10.0") throw new ArgumentException("BGCS 3.0 requires net10.0 consumers.");
        if (configuration.api.maximumValueStringCodeUnits <= 0) throw new ArgumentException("The native value-string copy bound must be positive.");
        if (configuration.runtime.mode is not ("package" or "source")) throw new ArgumentException("Runtime mode must be package or source.");
        if (configuration.runtime.sourceFeatures.IsDefault) throw new ArgumentException("Runtime source features must be an explicit array.");
        if (configuration.runtime.mode != "source" && !configuration.runtime.sourceFeatures.IsEmpty)
            throw new ArgumentException("Additional Runtime source features require runtime.mode=source.");
        var knownRuntimeFeatures = BGCS.CSharp.Emission.RuntimeSourceCatalog.GetFeatureIds();
        if (configuration.runtime.sourceFeatures.Distinct(StringComparer.Ordinal).Count() != configuration.runtime.sourceFeatures.Length
            || configuration.runtime.sourceFeatures.Any(feature => !knownRuntimeFeatures.Contains(feature, StringComparer.Ordinal)))
            throw new ArgumentException("Runtime source features must be unique exact registered catalog identities.");
        if (configuration.output.layout is not ("split" or "singleFile")) throw new ArgumentException("Output layout must be split or singleFile.");
        ConfiguredSourceProcessors.Validate(configuration.output);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.output.root);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.output.singleFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.cache.directory);
        if (configuration.cache.maximumBytes <= 0 || configuration.cache.maximumEntryBytes <= 0
            || configuration.cache.maximumEntryBytes > 268435456 || configuration.cache.maximumBytes < configuration.cache.maximumEntryBytes)
            throw new ArgumentException("Generation cache budgets must be positive and fit at least one entry of at most 256 MiB.");
        ArgumentNullException.ThrowIfNull(configuration.execution.diagnostics);
        if (configuration.execution.diagnostics.errorCodes.IsDefault)
            throw new ArgumentException("Diagnostic error codes must be an initialized immutable array.");
        HashSet<string> diagnosticCodes = new(StringComparer.Ordinal);
        foreach (string code in configuration.execution.diagnostics.errorCodes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            if (code.Length > 256 || code.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) || !diagnosticCodes.Add(code))
                throw new ArgumentException("Diagnostic error codes must be unique exact non-whitespace identities of at most 256 characters.");
        }
        if (configuration.execution.sessionTimeoutSeconds <= 0 || configuration.execution.sessionTimeoutSeconds > 4_294_967)
            throw new ArgumentException("The generation session timeout must be positive and fit the cancellation timer range.");
        string standard = configuration.source.languageStandard ?? (configuration.source.language == BGCS.Core.Inputs.SourceLanguage.Cpp ? "c++23" : "c17");
        ArgumentException.ThrowIfNullOrWhiteSpace(standard);
        bool cppStandard = standard.StartsWith("c++", StringComparison.Ordinal) || standard.StartsWith("gnu++", StringComparison.Ordinal);
        bool cStandard = standard.StartsWith("iso9899:", StringComparison.Ordinal)
            || standard.Length > 1 && standard[0] == 'c' && char.IsAsciiDigit(standard[1])
            || standard.Length > 3 && standard.StartsWith("gnu", StringComparison.Ordinal) && char.IsAsciiDigit(standard[3]);
        if (configuration.source.language == BGCS.Core.Inputs.SourceLanguage.Cpp && cStandard
            || configuration.source.language == BGCS.Core.Inputs.SourceLanguage.C && cppStandard)
            throw new ArgumentException("source.languageStandard belongs to a different source.language. Choose its matching standard or reset languageStandard to null to select that language's default.");
        return configuration with
        {
            source = configuration.source with { languageStandard = standard },
            extensions = configuration.extensions with { options = configuration.extensions.options.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { values = pair.Value.values.Clone() }, StringComparer.Ordinal) }
        };
    }

    private static void ValidatePointerName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string value = name.StartsWith('@') ? name[1..] : name;
        bool keyword = Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(value) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None;
        if (!keyword && !Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(value))
            throw new ArgumentException("A pointer presentation name must be one C# identifier.", nameof(name));
    }
}
