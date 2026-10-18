using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Text.Json;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;

namespace BGCS.Configuration;

/// <summary>Groups immutable BGCS 3.0 generation choices without retaining services or parser state.</summary>
public sealed record BindingConfiguration
{
    /// <summary>Gets the optional editor schema reference.</summary>
    [JsonPropertyName("$schema")]
    public string? schema { get; init; }
    /// <summary>Gets the explicitly declared configuration format revision.</summary>
    [JsonRequired]
    public int schemaVersion { get; init; }
    /// <summary>Gets ordered local base configuration references with unambiguous local path bases.</summary>
    public ImmutableArray<ConfigurationReference> extends { get; init; } = [];
    /// <summary>Gets the optional selected named preset applied before base and current document overrides.</summary>
    public string? profile { get; init; }
    /// <summary>Gets named strict configuration fragments owned by their declaring document.</summary>
    public ImmutableDictionary<string, JsonElement> profiles { get; init; } = ImmutableDictionary<string, JsonElement>.Empty;
    /// <summary>Gets stable native library identities.</summary>
    public LibraryConfiguration library { get; init; } = new();
    /// <summary>Gets source language and ordered compiler inputs.</summary>
    public SourceConfiguration source { get; init; } = new();
    /// <summary>Gets independently resolved native targets.</summary>
    public ImmutableArray<TargetConfiguration> targets { get; init; } = [new()];
    /// <summary>Gets managed presentation and import choices.</summary>
    public ApiConfiguration api { get; init; } = new();
    /// <summary>Gets explicit native semantic assertions; configuration never creates Verified evidence.</summary>
    public SemanticsConfiguration semantics { get; init; } = new();
    /// <summary>Gets explicit C++ semantic lowering and template requests.</summary>
    public CppConfiguration cpp { get; init; } = new();
    /// <summary>Gets package or standalone runtime delivery.</summary>
    public RuntimeConfiguration runtime { get; init; } = new();
    /// <summary>Gets output layout choices.</summary>
    public OutputConfiguration output { get; init; } = new();
    /// <summary>Gets cache policy.</summary>
    public CacheConfiguration cache { get; init; } = new();
    /// <summary>Gets session execution limits.</summary>
    public ExecutionConfiguration execution { get; init; } = new();
    /// <summary>Gets explicit neutral plugin input declarations.</summary>
    public ExtensionsConfiguration extensions { get; init; } = new();
}

/// <summary>Declares typed C++ bridge inputs before native parsing.</summary>
public sealed record CppConfiguration
{
    /// <summary>Gets exact user-asserted finite string, sequence, associative, optional, variant, expected, smart pointer, path and chrono protocols without substituting native storage.</summary>
    public ImmutableArray<CppFamilyBinding> familyBindings { get; init; } = [];
    /// <summary>Gets concrete class and fixed function template instantiations.</summary>
    public TemplateInstantiationRequest templateInstantiations { get; init; } = new();
    /// <summary>Gets typed native representation recipes with exact controlled source fragments.</summary>
    public ImmutableArray<TypeLoweringRecipe> typeLowerings { get; init; } = [];
    /// <summary>Gets explicitly selected native callable wrappers, export names and exclusions.</summary>
    public ImmutableArray<CallableLoweringRecipe> callableLowerings { get; init; } = [];
    /// <summary>Gets public C ABI shim headers and source inputs that use the ordinary parse and generation stages.</summary>
    public ImmutableArray<NativeShimRequest> nativeShims { get; init; } = [];
    /// <summary>Gets explicitly selected abstract interfaces for finite native callback proxy generation.</summary>
    public ImmutableArray<VirtualCallbackRequest> virtualCallbacks { get; init; } = [];
    /// <summary>Gets explicitly requested finite data-and-length input adapters while preserving primary opaque exports.</summary>
    public ImmutableArray<SequenceInputAdapterRequest> sequenceInputAdapters { get; init; } = [];
}

/// <summary>Declares local plugin manifests without embedding implementation services in configuration.</summary>
public sealed record ExtensionsConfiguration
{
    /// <summary>Gets plugin manifests whose paths are resolved from their declaring configuration document.</summary>
    public ImmutableArray<string> plugins { get; init; } = [];
    /// <summary>Gets strict settings objects keyed by the owning plugin identity.</summary>
    public ImmutableDictionary<string, PluginSettingsConfiguration> options { get; init; } = ImmutableDictionary<string, PluginSettingsConfiguration>.Empty;
}

/// <summary>Preserves a plugin's values and the declaring document's local input base.</summary>
public sealed record PluginSettingsConfiguration
{
    /// <summary>Gets the plugin-specific settings object validated by the registered plugin schema.</summary>
    public JsonElement values { get; init; } = JsonSerializer.SerializeToElement(ImmutableDictionary<string, string>.Empty);
    /// <summary>Gets the settings input base, or null to capture the SDK request base.</summary>
    public string? pathBase { get; init; }
    /// <summary>Gets detached JSON-pointer origins computed during input composition, or explicitly supplied by an SDK.</summary>
    [JsonIgnore]
    public ImmutableDictionary<string, string> valuePathBases { get; init; } = ImmutableDictionary<string, string>.Empty;
}

/// <summary>Identifies a logical library and its native module.</summary>
public sealed record LibraryConfiguration
{
    /// <summary>Gets the stable logical library identity.</summary>
    public string id { get; init; } = string.Empty;
    /// <summary>Gets the dynamic import name or static module identity.</summary>
    public string nativeName { get; init; } = string.Empty;
    /// <summary>Gets an optional unreserved C bridge prefix; null derives it from the logical library identity.</summary>
    public string? bridgeExportPrefix { get; init; }
}

/// <summary>Declares ordered native source inputs before parsing begins.</summary>
public sealed record SourceConfiguration
{
    /// <summary>Gets C or C++ source language.</summary>
    public SourceLanguage language { get; init; } = SourceLanguage.C;
    /// <summary>Gets the language standard; null selects C17 or C++23.</summary>
    public string? languageStandard { get; init; }
    /// <summary>Gets ordered source entry headers.</summary>
    public ImmutableArray<string> headers { get; init; } = [];
    /// <summary>Gets exact public header and declaration selection with mandatory native dependency closure.</summary>
    public DeclarationSelectionRequest selection { get; init; } = new();
    /// <summary>Gets whether selected C static, inline and direct-call-only helpers receive native adapters that must be compiled into the configured native module; defaults to false.</summary>
    public bool includeHeaderHelpers { get; init; }
    /// <summary>Gets explicitly named fixed native adapters for selected variadic functions; the native compiler applies default argument promotions.</summary>
    public ImmutableArray<VariadicVariantRequest> variadicVariants { get; init; } = [];
    /// <summary>Gets user include directories in search order.</summary>
    public ImmutableArray<string> includeDirectories { get; init; } = [];
    /// <summary>Gets system include directories in search order.</summary>
    public ImmutableArray<string> systemIncludeDirectories { get; init; } = [];
    /// <summary>Gets preprocessor definitions.</summary>
    public ImmutableArray<string> defines { get; init; } = [];
    /// <summary>Gets explicit parser driver arguments.</summary>
    public ImmutableArray<string> arguments { get; init; } = [];
    /// <summary>Gets source text prepended to entry headers.</summary>
    public string preamble { get; init; } = string.Empty;
    /// <summary>Gets source text appended to entry headers.</summary>
    public string postamble { get; init; } = string.Empty;
    /// <summary>Gets whether transitive declarations are retained.</summary>
    public bool includeTransitivelyReferencedHeaders { get; init; } = true;
    /// <summary>Gets whether system declarations are retained.</summary>
    public bool includeSystemDeclarations { get; init; }
    /// <summary>Gets whether macro facts are retained.</summary>
    public bool parseMacros { get; init; } = true;
    /// <summary>Gets whether source documentation is retained.</summary>
    public bool parseComments { get; init; } = true;
}

/// <summary>Separates a native target from the host parser and explicit SDK settings.</summary>
public sealed record TargetConfiguration
{
    /// <summary>Gets the requested native target, resolved from host by default.</summary>
    public string id { get; init; } = "host";
    /// <summary>Gets an explicit compiler triple compatible with this target's CPU, platform and ABI; null uses provider defaults.</summary>
    public string? triple { get; init; }
    /// <summary>Gets explicit SDK paths, arguments and compiler identity.</summary>
    public ToolchainConfiguration settings { get; init; } = new();
}

/// <summary>Stores target compiler and SDK inputs as immutable configuration values.</summary>
public sealed record ToolchainConfiguration
{
    /// <summary>Gets an optional read-only compiler installation hint; the generator does not execute it.</summary>
    public string? compilerPath { get; init; }
    /// <summary>Gets the target sysroot.</summary>
    public string? sysRoot { get; init; }
    /// <summary>Gets target system header roots.</summary>
    public ImmutableArray<string> systemIncludeDirectories { get; init; } = [];
    /// <summary>Gets target C++ standard library header roots.</summary>
    public ImmutableArray<string> cxxSystemIncludeDirectories { get; init; } = [];
    /// <summary>Gets target preprocessor definitions.</summary>
    public ImmutableArray<string> defines { get; init; } = [];
    /// <summary>Gets target parser arguments.</summary>
    public ImmutableArray<string> arguments { get; init; } = [];
}

/// <summary>Specifies managed API identity independently of frontend representation.</summary>
public sealed record ApiConfiguration
{
    /// <summary>Gets whether canonical RawApi and native imports receive native function, return and parameter name/type annotations.</summary>
    public bool emitNativeMetadata { get; init; }
    /// <summary>Gets whether native storage, managed enums and named ABI delegates are nested inside the public API.</summary>
    public bool nestGeneratedTypesInApi { get; init; }
    /// <summary>Gets whether missing native declaration documentation receives an inert placeholder summary.</summary>
    public bool emitPlaceholderComments { get; init; }
    /// <summary>Gets whether selected named callback typedefs also expose standalone exact ABI delegate declarations; raw imports remain function pointers.</summary>
    public bool emitCallbackDelegates { get; init; } = true;
    /// <summary>Gets whether public contexts/facades expose the configured NativeLibraryName constant.</summary>
    public bool emitLibraryNameConstant { get; init; } = true;
    /// <summary>Gets an optional qualified consumer string constant for DllImport or LibraryImport; its frozen value must match the resolved import name.</summary>
    public string? importLibraryNameConstant { get; init; }
    /// <summary>Gets asserted presentation overrides for existing native scalar or string constants.</summary>
    public ImmutableArray<ManagedConstantRuleRequest> constants { get; init; } = [];
    /// <summary>Gets existing native enum names, documentation and checked member-value presentation.</summary>
    public ImmutableArray<ManagedNativeEnumRuleRequest> nativeEnums { get; init; } = [];
    /// <summary>Gets explicit additional namespaces for all generated binding source files.</summary>
    public ImmutableArray<string> usings { get; init; } = [];
    /// <summary>Gets optional category casing, affix and reserved-name policies.</summary>
    public ManagedNamingPolicy naming { get; init; } = new();
    /// <summary>Gets exact callable presentation and typed constant-argument conveniences.</summary>
    public ImmutableArray<ManagedFunctionRuleRequest> functions { get; init; } = [];
    /// <summary>Gets optional exact record names, documentation and data-carrier conveniences.</summary>
    public ImmutableArray<ManagedRecordRuleRequest> records { get; init; } = [];
    /// <summary>Gets the bound for default-argument overloads in each context or named function view.</summary>
    public int defaultOverloadLimit { get; init; } = 128;
    /// <summary>Gets checked field-copy projections into consumer types without replacing raw native ABI carriers.</summary>
    public ImmutableArray<ExternalTypeConfiguration> externalTypes { get; init; } = [];
    /// <summary>Gets explicit metadata and source inputs for static projection analysis without building or executing consumers.</summary>
    public ManagedProjectionInputConfiguration projectionInputs { get; init; } = new();
    /// <summary>Gets additional managed names for existing exports, source signatures or unambiguous qualified functions.</summary>
    public ImmutableArray<ManagedFunctionAliasRequest> functionAliases { get; init; } = [];
    /// <summary>Gets named instance views of existing friendly operations without duplicating imports.</summary>
    public ImmutableArray<ManagedFunctionContainerRequest> functionContainers { get; init; } = [];
    /// <summary>Gets explicit C receiver/member mappings without changing native source ownership facts.</summary>
    public ImmutableArray<ManagedReceiverRequest> memberFunctions { get; init; } = [];
    /// <summary>Gets exact non-owning native address-carrier declarations.</summary>
    public ImmutableArray<ManagedPointerCarrierRequest> pointerCarriers { get; init; } = [];
    /// <summary>Gets explicit pointer-address callable presentation without replacing tracked resource results.</summary>
    public ImmutableArray<ManagedPointerFunctionRequest> pointerFunctions { get; init; } = [];
    /// <summary>Gets the managed namespace.</summary>
    public string @namespace { get; init; } = "Bindings";
    /// <summary>Gets the public context name.</summary>
    public string name { get; init; } = "NativeApi";
    /// <summary>Gets the instance or explicitly shared static API style.</summary>
    public ManagedApiStyle style { get; init; } = ManagedApiStyle.Instance;
    /// <summary>Gets the native import mechanism.</summary>
    public BindingImportMode importMode { get; init; } = BindingImportMode.FunctionTable;
    /// <summary>Gets the consumer framework baseline.</summary>
    public string framework { get; init; } = "net10.0";
    /// <summary>Gets the positive code-unit bound for native string/path values copied and destroyed within one creating call.</summary>
    public int maximumValueStringCodeUnits { get; init; } = 1048576;
    /// <summary>Gets the optional managed execution model; null resolves from the concrete native target.</summary>
    public ManagedRuntimeKind? runtime { get; init; }
    /// <summary>Gets explicit native-to-managed name mappings.</summary>
    public ImmutableDictionary<string, string> names { get; init; } = ImmutableDictionary<string, string>.Empty;
    /// <summary>Gets explicit managed enum declarations with checked fixed-width values.</summary>
    public ImmutableArray<ManagedEnumDefinition> enums { get; init; } = [];
    /// <summary>Gets exact native scalar names mapped to declared managed enum names without changing the raw ABI.</summary>
    public ImmutableDictionary<string, string> enumMappings { get; init; } = ImmutableDictionary<string, string>.Empty;
}

/// <summary>Identifies runtime delivery without adding generator dependencies to consumers.</summary>
public sealed record RuntimeConfiguration
{
    /// <summary>Gets package or source delivery.</summary>
    public string mode { get; init; } = "package";
    /// <summary>Gets the optional standalone namespace; null selects an API-local namespace.</summary>
    public string? @namespace { get; init; }
    /// <summary>Gets additional source catalog features; runtime.complete includes the complete public Runtime implementation.</summary>
    public ImmutableArray<string> sourceFeatures { get; init; } = [];
}

/// <summary>Groups explicit native semantic assertions independently of source parsing.</summary>
public sealed record SemanticsConfiguration
{
    /// <summary>Gets explicit borrowed native parameter lifetime and retention assertions.</summary>
    public ImmutableArray<BorrowedParameterConfiguration> borrowedParameters { get; init; } = [];
    /// <summary>Gets explicit borrowed result validity assertions; these never transfer native destruction responsibility.</summary>
    public ImmutableArray<BorrowedResultConfiguration> borrowedResults { get; init; } = [];
    /// <summary>Gets explicit native callback lifetime, threading, user-data and exception fallback contracts.</summary>
    public ImmutableArray<CallbackParameterConfiguration> callbackParameters { get; init; } = [];
    /// <summary>Gets explicit native completion/cancellation protocols projected through retained operation resources.</summary>
    public ImmutableArray<AsyncOperationConfiguration> asyncOperations { get; init; } = [];
    /// <summary>Gets explicitly selected native text parameter contracts.</summary>
    public ImmutableArray<StringParameterConfiguration> stringParameters { get; init; } = [];
    /// <summary>Gets explicit encoding, extent and null contracts for direct native text results.</summary>
    public ImmutableArray<StringResultConfiguration> stringResults { get; init; } = [];
    /// <summary>Gets explicit owned result release and thread-domain assertions.</summary>
    public ImmutableArray<OwnedResultConfiguration> ownedResults { get; init; } = [];
    /// <summary>Gets explicitly bounded native buffer parameter contracts.</summary>
    public ImmutableArray<BufferParameterConfiguration> bufferParameters { get; init; } = [];
    /// <summary>Gets explicit one-value pointer direction and synchronous address-lifetime assertions.</summary>
    public ImmutableArray<ValueParameterConfiguration> valueParameters { get; init; } = [];
}

/// <summary>Asserts a borrowed native pointer's lifetime independently of member presentation.</summary>
public sealed record BorrowedParameterConfiguration
{
    /// <summary>Gets the exact native operation selector.</summary>
    public string function { get; init; } = string.Empty;
    /// <summary>Gets the native pointer parameter name.</summary>
    public string parameter { get; init; } = string.Empty;
    /// <summary>Gets the declared native pointer lifetime.</summary>
    public NativeLifetimeKind lifetime { get; init; }
    /// <summary>Gets native address retention, or null when unknown.</summary>
    public bool? retainsPointer { get; init; }
}

/// <summary>Asserts that a returned native pointer remains valid through the supplying module's actual unload.</summary>
public sealed record BorrowedResultConfiguration
{
    /// <summary>Gets the exact native operation selector.</summary>
    public string function { get; init; } = string.Empty;
    /// <summary>Gets the explicit address validity boundary; module borrowing requires Module.</summary>
    public NativeLifetimeKind lifetime { get; init; }
}

/// <summary>Declares a single native value pointer without inferring input, output or retention semantics.</summary>
public sealed record ValueParameterConfiguration
{
    /// <summary>Gets the exact export or unambiguous original callable identity.</summary>
    public string function { get; init; } = string.Empty;
    /// <summary>Gets the original native parameter name.</summary>
    public string parameter { get; init; } = string.Empty;
    /// <summary>Gets explicit native access direction.</summary>
    public NativeParameterDirection direction { get; init; }
    /// <summary>Gets the exact native address validity boundary.</summary>
    public NativeLifetimeKind lifetime { get; init; }
    /// <summary>Gets address retention, or null when unknown.</summary>
    public bool? retainsPointer { get; init; }
    /// <summary>Gets null allowance; a required typed value requires explicit false.</summary>
    public bool? allowsNull { get; init; }
    /// <summary>Gets an optional stricter native address alignment.</summary>
    public int? requiredAlignment { get; init; }
}

/// <summary>Declares one pointer's exact bounds and synchronous storage access.</summary>
public sealed record BufferParameterConfiguration
{
    /// <summary>Gets the exact export or unambiguous original qualified callable name.</summary>
    public string function { get; init; } = string.Empty;
    /// <summary>Gets the C or original source parameter name.</summary>
    public string parameter { get; init; } = string.Empty;
    /// <summary>Gets the scalar native length parameter name.</summary>
    public string? lengthParameter { get; init; }
    /// <summary>Gets an optional scalar writable capacity parameter name.</summary>
    public string? capacityParameter { get; init; }
    /// <summary>Gets an optional output integral-count pointer name using the declared native units.</summary>
    public string? writtenCountParameter { get; init; }
    /// <summary>Gets the native length unit.</summary>
    public NativeLengthUnit lengthUnit { get; init; }
    /// <summary>Gets native storage access direction.</summary>
    public NativeParameterDirection direction { get; init; }
    /// <summary>Gets the explicitly selected native access lifetime.</summary>
    public NativeLifetimeKind lifetime { get; init; }
    /// <summary>Gets native pointer retention, or null when unknown.</summary>
    public bool? retainsPointer { get; init; }
    /// <summary>Gets native null allowance, or null when unknown.</summary>
    public bool? allowsNull { get; init; }
    /// <summary>Gets an optional stricter native address alignment.</summary>
    public int? requiredAlignment { get; init; }
}

/// <summary>Declares native result cleanup without inferring ownership from a function name.</summary>
public sealed record OwnedResultConfiguration
{
    /// <summary>Gets the exact C export or unambiguous original qualified callable name.</summary>
    public string function { get; init; } = string.Empty;
    /// <summary>Gets the matching release callable, or null to retain a declared lowering release.</summary>
    public string? releaseFunction { get; init; }
    /// <summary>Gets the allocation and release domain, or null to retain a declared lowering allocator.</summary>
    public string? releaseDomain { get; init; }
    /// <summary>Gets any for thread-independent cleanup, or unknown to omit automatic ownership wrappers.</summary>
    public string releaseThreadDomain { get; init; } = "unknown";
}

/// <summary>Declares text facts for one exact C export parameter without claiming independent verification.</summary>
public sealed record StringParameterConfiguration
{
    /// <summary>Gets the exact C export name.</summary>
    public string function { get; init; } = string.Empty;
    /// <summary>Gets the original native parameter name.</summary>
    public string parameter { get; init; } = string.Empty;
    /// <summary>Gets the explicitly selected native encoding.</summary>
    public NativeTextEncoding encoding { get; init; }
    /// <summary>Gets the native code-unit width.</summary>
    public int codeUnitWidth { get; init; }
    /// <summary>Gets the explicit multibyte code-unit byte order.</summary>
    public NativeTextByteOrder byteOrder { get; init; }
    /// <summary>Gets the native length unit.</summary>
    public NativeLengthUnit lengthUnit { get; init; }
    /// <summary>Gets native read and write access.</summary>
    public NativeParameterDirection direction { get; init; }
    /// <summary>Gets the native pointer validity boundary.</summary>
    public NativeLifetimeKind lifetime { get; init; }
    /// <summary>Gets zero termination, or null when unknown.</summary>
    public bool? zeroTerminated { get; init; }
    /// <summary>Gets pointer retention, or null when unknown.</summary>
    public bool? retainsPointer { get; init; }
    /// <summary>Gets null allowance, or null when unknown.</summary>
    public bool? allowsNull { get; init; }
    /// <summary>Gets an optional native length parameter name.</summary>
    public string? lengthParameter { get; init; }
    /// <summary>Gets the explicit scalar input length direction; Unknown withholds counted string conversion.</summary>
    public NativeParameterDirection lengthDirection { get; init; }
    /// <summary>Gets the positive encoded code-unit allocation bound for counted input, excluding extra termination.</summary>
    public int maximumCodeUnits { get; init; } = 1048576;
}

/// <summary>Declares a direct text result whose ownership is supplied by an explicit owned or borrowed result rule.</summary>
public sealed record StringResultConfiguration
{
    /// <summary>Gets the exact selected native callable.</summary>
    public string function { get; init; } = string.Empty;
    /// <summary>Gets the declared native UTF encoding.</summary>
    public NativeTextEncoding encoding { get; init; }
    /// <summary>Gets an optional explicit code-unit width; null selects the width defined by the encoding, still checked against the native pointee.</summary>
    public int? codeUnitWidth { get; init; }
    /// <summary>Gets multibyte code-unit order; the default declares native order.</summary>
    public NativeTextByteOrder byteOrder { get; init; } = NativeTextByteOrder.Native;
    /// <summary>Gets the unit of an explicit length channel or fixed length.</summary>
    public NativeLengthUnit lengthUnit { get; init; } = NativeLengthUnit.CodeUnits;
    /// <summary>Gets the explicit zero-termination guarantee; exact-length results do not require a terminator.</summary>
    public bool? zeroTerminated { get; init; }
    /// <summary>Gets explicit native null allowance.</summary>
    public bool? allowsNull { get; init; }
    /// <summary>Gets a fixed exact length excluding any trailing terminator, in the declared units.</summary>
    public int? fixedLength { get; init; }
    /// <summary>Gets an input scalar or output integral-pointer parameter supplying the exact length excluding any terminator.</summary>
    public string? lengthParameter { get; init; }
    /// <summary>Gets the explicit length channel direction.</summary>
    public NativeParameterDirection lengthDirection { get; init; }
    /// <summary>Gets the explicit guarantee that output length storage is written during this call and never retained.</summary>
    public bool? lengthOutputCallBounded { get; init; }
    /// <summary>Gets the scan or copy cap in code units; override the default for larger valid native strings.</summary>
    public int maximumCodeUnits { get; init; } = 1048576;
}

/// <summary>Declares immutable metadata and source inputs for static managed projection analysis.</summary>
public sealed record ManagedProjectionInputConfiguration
{
    /// <summary>Gets explicit managed assembly reference paths.</summary>
    public ImmutableArray<string> references { get; init; } = [];
    /// <summary>Gets extra source files required by configured managed mappings.</summary>
    public ImmutableArray<string> sources { get; init; } = [];
    /// <summary>Gets additional compile-time symbols.</summary>
    public ImmutableArray<string> defines { get; init; } = [];
}

/// <summary>Declares one jointly published output root and managed file layout.</summary>
public sealed record OutputConfiguration
{
    /// <summary>Gets immutable configured managed source operations, applied before final contract checks and publication.</summary>
    public ImmutableArray<SourceProcessorConfiguration> processors { get; init; } = [];
    /// <summary>Gets the configuration-relative formal output root.</summary>
    public string root { get; init; } = "Generated";
    /// <summary>Gets split or singleFile managed layout.</summary>
    public string layout { get; init; } = "split";
    /// <summary>Gets the optional single managed filename.</summary>
    public string singleFileName { get; init; } = "Bindings.cs";
}

/// <summary>Declares caching policy without embedding a mutable cache service.</summary>
public sealed record CacheConfiguration
{
    /// <summary>Gets whether proven generation cache entries may be used.</summary>
    public bool enabled { get; init; } = true;
    /// <summary>Gets the configuration-relative cache root.</summary>
    public string directory { get; init; } = ".bgcs/cache";
    /// <summary>Gets the bounded total disk budget for reusable generation-stage artifacts.</summary>
    public long maximumBytes { get; init; } = 536870912;
    /// <summary>Gets the largest serialized reusable entry, at most 256 MiB.</summary>
    public int maximumEntryBytes { get; init; } = 67108864;
}

/// <summary>Declares session deadlines and diagnostic gates independently of platform facts.</summary>
public sealed record ExecutionConfiguration
{
    /// <summary>Gets pre-publication warning promotion; native ABI errors cannot be suppressed.</summary>
    public DiagnosticPolicyConfiguration diagnostics { get; init; } = new();
    /// <summary>Gets the total generation session timeout in seconds.</summary>
    public int sessionTimeoutSeconds { get; init; } = 1800;
}

/// <summary>Promotes selected warnings into publication-blocking errors without downgrading errors.</summary>
public sealed record DiagnosticPolicyConfiguration
{
    /// <summary>Gets whether every warning observed by a candidate gate becomes an error.</summary>
    public bool warningsAsErrors { get; init; }
    /// <summary>Gets exact warning codes promoted to errors even when global warning promotion is disabled.</summary>
    public ImmutableArray<string> errorCodes { get; init; } = [];
}
