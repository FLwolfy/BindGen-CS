using System.Collections.Immutable;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;

namespace BGCS.CSharp.Planning;

/// <summary>Freezes C# carriers and import signatures before source text is emitted.</summary>
/// <param name="api">The managed API and backing native ABI.</param>
/// <param name="importMode">The selected import mechanism.</param>
/// <param name="types">Planned ABI carriers.</param>
/// <param name="methods">Planned native import signatures.</param>
public sealed record CSharpEmissionPlan(
    ManagedApiModel api,
    BindingImportMode importMode,
    ImmutableArray<CSharpTypePlan> types,
    ImmutableArray<CSharpMethodPlan> methods
)
{
    /// <summary>Gets explicit runtime-specific physical import carriers whose exact typed API is retained through checked address conversions.</summary>
    public ImmutableArray<CSharpPhysicalImportPlan> physicalImports { get; init; } = [];
    /// <summary>Gets an independently compiler-checked external constant for static native import attributes.</summary>
    public CSharpImportLibraryNamePlan? importLibraryName { get; init; }
    /// <summary>Gets exact non-owning address views whose structs never replace native import carriers.</summary>
    public ImmutableArray<CSharpPointerCarrierPlan> pointerCarriers { get; init; } = [];
    /// <summary>Gets explicit native address forwarding with checked ordinary input conversions.</summary>
    public ImmutableArray<CSharpPointerFunctionPlan> pointerFunctions { get; init; } = [];
    /// <summary>Gets the independently owned concrete context type, including for an explicitly shared static facade.</summary>
    public string contextName => CSharpNames.Identifier(api.style == ManagedApiStyle.Static ? api.name.TrimStart('@') + "Context" : api.name);
    /// <summary>Gets the selected consumer runtime namespace used by all lifetime and cleanup plans.</summary>
    public string runtimeNamespace { get; init; } = "BGCS.Runtime";
    /// <summary>Gets checked managed forwarders sharing existing native imports and conversion plans.</summary>
    public ImmutableArray<CSharpAliasPlan> aliases { get; init; } = [];
    /// <summary>Gets allocation-free named views forwarding to checked context operations.</summary>
    public ImmutableArray<CSharpFunctionContainerPlan> functionContainers { get; init; } = [];
    /// <summary>Gets finite constant-argument overloads forwarding through existing friendly operations.</summary>
    public ImmutableArray<CSharpDefaultOverloadPlan> defaultOverloads { get; init; } = [];
    /// <summary>Gets checked operations whose explicitly nullable raw inputs are permanently bound to constant null.</summary>
    public ImmutableArray<CSharpNullDefaultMethodPlan> nullDefaultMethods { get; init; } = [];
    /// <summary>Gets finite alternate public input signatures forwarding through existing checked operations.</summary>
    public ImmutableArray<CSharpInputVariationPlan> inputVariations { get; init; } = [];
    /// <summary>Gets qualified default references that the C# compiler must independently accept as constants.</summary>
    public ImmutableArray<CSharpDefaultConstantPlan> defaultConstants { get; init; } = [];
    /// <summary>Gets finite managed virtual callback projections requiring explicit caller stop-and-join.</summary>
    public ImmutableArray<CSharpVirtualProxyPlan> virtualProxies { get; init; } = [];
    /// <summary>Gets optional managed callback omissions while retaining the checked raw ABI.</summary>
    public ImmutableArray<CSharpVirtualProxyOmissionPlan> virtualProxyOmissions { get; init; } = [];
    /// <summary>Gets synchronous managed callback wrappers with explicit native call-return quiescence.</summary>
    public ImmutableArray<CSharpCallbackMethodPlan> callScopedCallbacks { get; init; } = [];
    /// <summary>Gets returned-handle callback registrations with explicit native stop-and-join and module resource retention.</summary>
    public ImmutableArray<CSharpRetainedCallbackPlan> retainedCallbacks { get; init; } = [];
    /// <summary>Gets explicit native completion/cancellation operations retaining memory and modules through terminal native join.</summary>
    public ImmutableArray<CSharpAsyncOperationPlan> asyncOperations { get; init; } = [];
    /// <summary>Gets explicit reasons why annotated native callbacks retain only their checked raw ABI.</summary>
    public ImmutableArray<CSharpCallbackOmissionPlan> callbackOmissions { get; init; } = [];
    /// <summary>Gets named exact native delegate declarations without implying a registration lifetime.</summary>
    public ImmutableArray<CSharpAbiDelegatePlan> abiDelegates { get; init; } = [];
    /// <summary>Gets unsupported typed-value conveniences whose exact raw pointer ABI remains available.</summary>
    public ImmutableArray<CSharpValueParameterOmissionPlan> valueParameterOmissions { get; init; } = [];
    /// <summary>Gets the explicitly planned static forwarding and shared lifecycle contract, or null for instance APIs.</summary>
    public CSharpSharedContextPlan? sharedContext { get; init; }
    /// <summary>Gets resolved runtime constraints independently of target names.</summary>
    public ManagedRuntimeProfile? managedRuntime { get; init; }
    /// <summary>Gets the exact native table adapter protocol checked after artifact processing.</summary>
    public StaticExportTableDescriptor? staticExportTable { get; init; }
    /// <summary>Gets exact managed storage declarations planned before emission.</summary>
    public ImmutableArray<CSharpStoragePlan> storage { get; init; } = [];
    /// <summary>Gets verified status-channel wrappers whose error cleanup remains inside one call lease.</summary>
    public ImmutableArray<CSharpStatusMethodPlan> statusMethods { get; init; } = [];
    /// <summary>Gets typed owned object projections whose calls retain the resource and its module.</summary>
    public ImmutableArray<CSharpObjectPlan> objects { get; init; } = [];
    /// <summary>Gets direct C pointer factories with explicit matching cleanup and module resource leases.</summary>
    public ImmutableArray<CSharpDirectOwnedFactoryPlan> directOwnedFactories { get; init; } = [];
    /// <summary>Gets direct borrowed pointer returns whose declared address lifetime extends to actual module unload.</summary>
    public ImmutableArray<CSharpModuleBorrowedFactoryPlan> directBorrowedFactories { get; init; } = [];
    /// <summary>Gets native text results copied under an explicit extent, encoding and ownership protocol.</summary>
    public ImmutableArray<CSharpStringResultPlan> stringResults { get; init; } = [];
    /// <summary>Gets native value strings copied and destroyed synchronously under one creating-call lease.</summary>
    public ImmutableArray<CSharpValueStringResultPlan> valueStringResults { get; init; } = [];
    /// <summary>Gets unsupported owned factories or receiver conveniences while retaining the raw ABI.</summary>
    public ImmutableArray<CSharpOwnedProjectionOmissionPlan> ownedProjectionOmissions { get; init; } = [];
    /// <summary>Gets normalized managed recipe conversion helpers.</summary>
    public ImmutableArray<CSharpRecipeProjectionPlan> recipeProjections { get; init; } = [];
    /// <summary>Gets typed consumer field copies whose raw carrier remains independently planned.</summary>
    public ImmutableArray<CSharpExternalTypeProjectionPlan> externalTypeProjections { get; init; } = [];
    /// <summary>Gets public named conversion helper aliases for all configured recipes.</summary>
    public ImmutableArray<CSharpRecipeAliasPlan> recipeAliases { get; init; } = [];
    /// <summary>Gets public managed enums independently of the native ABI storage declarations.</summary>
    public ImmutableArray<CSharpManagedEnumPlan> managedEnums { get; init; } = [];
    /// <summary>Gets exact typed native source constant declarations.</summary>
    public ImmutableArray<CSharpConstantPlan> constants { get; init; } = [];
    /// <summary>Gets exact Unicode fields or readonly native-code-unit properties.</summary>
    public ImmutableArray<CSharpStringConstantPlan> stringConstants { get; init; } = [];
    /// <summary>Gets whether the verified browser LibraryImport profile uses its default Cdecl import convention.</summary>
    public bool libraryImportUsesPlatformDefault { get; init; }
    /// <summary>Gets aggregate ABI shapes checked separately from storage size and offsets.</summary>
    public ImmutableArray<CSharpAggregateCallPlan> aggregateCalls { get; init; } = [];
    /// <summary>Gets callable signatures retained only as opaque native addresses, never as invented callable C# prototypes.</summary>
    public ImmutableArray<CSharpOpaqueCallablePlan> opaqueCallables { get; init; } = [];
}

/// <summary>Plans one explicitly shared facade while retaining ordinary independent instance contexts.</summary>
/// <param name="name">The public static facade name.</param>
/// <param name="runtimeNamespace">The selected runtime namespace.</param>
/// <param name="canLoad">Whether dynamic native loading is implemented by this runtime profile.</param>
/// <param name="canFromStatic">Whether the native export-table adapter factory is available.</param>
/// <param name="methods">Checked friendly forwarding signatures.</param>
public sealed record CSharpSharedContextPlan(
    string name,
    string runtimeNamespace,
    bool canLoad,
    bool canFromStatic,
    ImmutableArray<CSharpFacadeMethodPlan> methods
);

/// <summary>Plans a static friendly forwarding method without duplicating interop conversions or cleanup.</summary>
/// <param name="name">The public method name.</param>
/// <param name="returnType">The public result type.</param>
/// <param name="parameterTypes">The public input types.</param>
/// <param name="parameterNames">Input names in forwarding order.</param>
public sealed record CSharpFacadeMethodPlan(
    string name,
    string returnType,
    ImmutableArray<string> parameterTypes,
    ImmutableArray<string> parameterNames
)
{
    /// <summary>Gets exact friendly in, ref or out modifiers aligned with public parameters; an empty array means ordinary value parameters.</summary>
    public ImmutableArray<string> parameterModifiers { get; init; } = [];
    /// <summary>Gets the forwarded native identity, or null for a managed-only factory.</summary>
    public StableId? nativeExport { get; init; }
    /// <summary>Gets raw parameter indices selected by this signature; minus one identifies an explicit managed context parameter.</summary>
    public ImmutableArray<int> inputIndices { get; init; } = [];
    /// <summary>Gets exact native inputs already bound to null under an explicit nullable synchronous value contract.</summary>
    public ImmutableArray<StableId> boundNativeNullInputs { get; init; } = [];
    /// <summary>Gets inert source documentation retained from the forwarded method.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets mapped native documentation sections for this forwarding signature.</summary>
    public CSharpCallableDocumentationPlan? documentationContent { get; init; }
}

/// <summary>Plans an alias as an exact forward to an existing checked operation.</summary>
/// <param name="targetIndex">The original native import index; no new native import is emitted.</param>
/// <param name="raw">The public raw forwarding signature.</param>
/// <param name="friendly">The optional existing friendly projection with its alias name.</param>
public sealed record CSharpAliasPlan(
    int targetIndex,
    CSharpMethodPlan raw,
    CSharpFacadeMethodPlan? friendly
);

/// <summary>Retains a native callback address when its invocation needs a separate native adapter.</summary>
/// <param name="nativeType">The source callable type with its original prototype intact.</param>
/// <param name="reason">The unsupported invocation capability; storing or passing its address does not invoke it.</param>
public sealed record CSharpOpaqueCallablePlan(
    StableId nativeType,
    string reason
);

/// <summary>Records a checked aggregate shape and the runtime calling algorithm that consumes it.</summary>
/// <param name="nativeType">The exact native record identity.</param>
/// <param name="callingAbi">The selected implemented calling algorithm.</param>
/// <param name="size">The complete native size.</param>
/// <param name="alignment">The matched managed and native alignment.</param>
/// <param name="eightByteClasses">System V eightbyte classes, or the Microsoft size-based argument/return form.</param>
public sealed record CSharpAggregateCallPlan(
    StableId nativeType,
    ManagedAggregateCallingAbi callingAbi,
    int size,
    int alignment,
    ImmutableArray<string> eightByteClasses
);

/// <summary>Plans an explicit managed enum without changing any raw ABI carrier.</summary>
/// <param name="name">The checked C# type name.</param>
/// <param name="underlyingType">The fixed-width C# integral carrier.</param>
/// <param name="size">The integral byte width.</param>
/// <param name="isSigned">The integral signedness.</param>
/// <param name="values">Checked decimal integral literals.</param>
/// <param name="isFlags">Whether the declaration carries the Flags attribute.</param>
public sealed record CSharpManagedEnumPlan(
    string name,
    string underlyingType,
    int size,
    bool isSigned,
    ImmutableArray<CSharpEnumValuePlan> values,
    bool isFlags
)
{
    /// <summary>Gets inert plain-text documentation for the configured managed declaration.</summary>
    public string? documentation { get; init; }
}

/// <summary>Plans a scalar or void friendly method with an explicit owned native error channel.</summary>
/// <param name="methodIndex">The exact imported method index.</param>
/// <param name="inputIndices">Public scalar inputs in native order.</param>
/// <param name="resultIndex">The original scalar result parameter, or minus one for void.</param>
/// <param name="errorIndex">The owned error output parameter.</param>
/// <param name="releaseIndex">The matching imported error-release operation.</param>
/// <param name="successValue">The exact native success status.</param>
/// <param name="resultType">The checked public scalar result type.</param>
public sealed record CSharpStatusMethodPlan(
    int methodIndex,
    ImmutableArray<int> inputIndices,
    int resultIndex,
    int errorIndex,
    int releaseIndex,
    int successValue,
    string resultType
)
{
    /// <summary>Gets an independently checked owned result and its cleanup protocol.</summary>
    public CSharpOwnedResultPlan? ownedResult { get; init; }
    /// <summary>Gets a native receiver parameter, or minus one for a context-level method.</summary>
    public int instanceIndex { get; init; } = -1;
}

/// <summary>Plans an owned pointer result whose native release can execute on any cleanup thread.</summary>
/// <param name="releaseIndex">The matching object release import.</param>
/// <param name="releaseDomain">The explicitly identified allocation and release domain.</param>
/// <param name="releaseErrorIndex">The release error output, or minus one for a void release.</param>
/// <param name="releaseErrorCleanupIndex">The release error cleanup import, or minus one.</param>
/// <param name="releaseSuccessValue">The native release success status.</param>
public sealed record CSharpOwnedResultPlan(
    int releaseIndex,
    string releaseDomain,
    int releaseErrorIndex,
    int releaseErrorCleanupIndex,
    int releaseSuccessValue
)
{
    /// <summary>Gets the checked nested object wrapper name, or null for an untyped resource.</summary>
    public string? objectName { get; init; }
}

/// <summary>Plans one typed object wrapper over an independently owned native handle.</summary>
/// <param name="name">The checked nested C# object name.</param>
/// <param name="nativeType">The original native class identity.</param>
/// <param name="methodIndices">Status-method plan indices that operate on this class.</param>
public sealed record CSharpObjectPlan(
    string name,
    StableId nativeType,
    ImmutableArray<int> methodIndices
)
{
    /// <summary>Gets whether this wrapper owns the native resource or only retains its supplying module.</summary>
    public ManagedReceiverKind kind { get; init; }
    /// <summary>Gets explicit direct C members, separate from checked C++ status-member indices.</summary>
    public ImmutableArray<CSharpReceiverMethodPlan> directMethods { get; init; } = [];
}

/// <summary>Describes an ABI carrier's C# spelling and optional declared storage.</summary>
/// <param name="nativeType">The source ABI identity.</param>
/// <param name="syntax">The C# ABI carrier spelling.</param>
/// <param name="declaredName">An optional declared storage type name.</param>
public sealed record CSharpTypePlan(
    StableId nativeType,
    string syntax,
    string? declaredName
);

/// <summary>Describes a native import's checked C# signature.</summary>
/// <param name="nativeExport">The native export identity.</param>
/// <param name="name">The managed method name.</param>
/// <param name="exportName">The exact native export symbol.</param>
/// <param name="returnType">The C# ABI return carrier.</param>
/// <param name="parameterTypes">C# ABI parameter carriers in order.</param>
/// <param name="parameterNames">Managed parameter names in order.</param>
/// <param name="callingConvention">The validated C# unmanaged convention name.</param>
public sealed record CSharpMethodPlan(
    StableId nativeExport,
    string name,
    string exportName,
    string returnType,
    ImmutableArray<string> parameterTypes,
    ImmutableArray<string> parameterNames,
    string callingConvention
)
{
    /// <summary>Gets inert native documentation shared by raw and projected method emission.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets parsed native documentation mapped to exact raw parameter indices.</summary>
    public CSharpCallableDocumentationPlan? documentationContent { get; init; }
    /// <summary>Gets independently checked temporary text allocation and conversion plans.</summary>
    public ImmutableArray<CSharpStringInputPlan> stringInputs { get; init; } = [];
    /// <summary>Gets frozen bounded span pinning and exact native length conversion plans.</summary>
    public ImmutableArray<CSharpBufferInputPlan> bufferInputs { get; init; } = [];
    /// <summary>Gets checked scalar length/capacity and optional output-count span protocols.</summary>
    public ImmutableArray<CSharpBufferProtocolPlan> bufferProtocols { get; init; } = [];
    /// <summary>Gets explicitly directed temporary one-value pointers with address validation.</summary>
    public ImmutableArray<CSharpValueParameterPlan> valueParameters { get; init; } = [];
    /// <summary>Gets explicit managed input or original-result conversions keyed by native parameter index.</summary>
    public ImmutableDictionary<int, CSharpRecipeProjectionPlan> valueProjections { get; init; } = ImmutableDictionary<int, CSharpRecipeProjectionPlan>.Empty;
    /// <summary>Gets an explicit managed direct-return conversion.</summary>
    public CSharpRecipeProjectionPlan? returnProjection { get; init; }
}

/// <summary>Maps native documentation sections to a checked managed signature without reinterpreting source markup.</summary>
/// <param name="summary">The native summary prose.</param>
/// <param name="parameters">Descriptions aligned with the containing plan's parameter indices.</param>
/// <param name="returns">The original native result description.</param>
/// <param name="notes">Unmapped source parameter and template descriptions retained as plain text.</param>
/// <param name="statusReturn">Whether the raw result represents an added status channel.</param>
public sealed record CSharpCallableDocumentationPlan(
    string summary,
    ImmutableArray<string?> parameters,
    string? returns,
    ImmutableArray<string> notes,
    bool statusReturn
);

/// <summary>Plans normalized explicit managed conversions while preserving the separate raw carrier.</summary>
/// <param name="id">The deterministic complete conversion identity.</param>
/// <param name="managedType">The checked public type syntax.</param>
/// <param name="carrierType">The checked raw native carrier syntax.</param>
/// <param name="toNativeExpression">The optional input expression over one helper parameter named value.</param>
/// <param name="fromNativeExpression">The optional output expression over one helper parameter named value.</param>
/// <param name="requiredNamespace">The explicitly required expression namespace.</param>
public sealed record CSharpRecipeProjectionPlan(
    string id,
    string managedType,
    string carrierType,
    string? toNativeExpression,
    string? fromNativeExpression,
    string? requiredNamespace
);

/// <summary>Plans one public helper name for a configured conversion.</summary>
/// <param name="name">The allocated portable managed helper suffix.</param>
/// <param name="projectionId">The complete normalized conversion identity.</param>
public sealed record CSharpRecipeAliasPlan(
    string name,
    string projectionId
);

/// <summary>Plans a synchronous pinned buffer while preserving native pointer aliasing.</summary>
/// <param name="parameterIndex">The native pointer parameter index.</param>
/// <param name="lengthIndex">The checked native scalar length index.</param>
/// <param name="elementType">The checked unmanaged C# element storage.</param>
/// <param name="lengthUnit">The declared byte or element length unit.</param>
/// <param name="direction">The declared native storage access.</param>
/// <param name="allowsNull">Whether an empty span may pass a null pointer.</param>
/// <param name="alignment">The address alignment checked inside the fixed scope.</param>
public sealed record CSharpBufferInputPlan(
    int parameterIndex,
    int lengthIndex,
    string elementType,
    NativeLengthUnit lengthUnit,
    NativeParameterDirection direction,
    bool allowsNull,
    int alignment
);

/// <summary>Plans one explicit native text input without inferring code-unit width from managed string storage.</summary>
/// <param name="parameterIndex">The native argument index.</param>
/// <param name="encoding">The declared UTF encoding.</param>
/// <param name="byteOrder">The declared multibyte code-unit order.</param>
/// <param name="allowsNull">Whether a null native text pointer is permitted.</param>
/// <param name="alignment">The complete native pointee allocation alignment.</param>
public sealed record CSharpStringInputPlan(
    int parameterIndex,
    NativeTextEncoding encoding,
    NativeTextByteOrder byteOrder,
    bool allowsNull,
    int alignment
)
{
    /// <summary>Gets the exact scalar count parameter position, or null for terminated text.</summary>
    public int? lengthIndex { get; init; }
    /// <summary>Gets byte or code-unit counting for the selected native length channel.</summary>
    public NativeLengthUnit lengthUnit { get; init; }
    /// <summary>Gets the encoded code-unit byte width independently of managed string storage.</summary>
    public int codeUnitWidth { get; init; }
    /// <summary>Gets the positive bound for counted input encoding, excluding extra termination.</summary>
    public int maximumCodeUnits { get; init; }
}

/// <summary>Plans a declared native storage carrier independently of source formatting.</summary>
/// <param name="nativeType">The native storage identity.</param>
/// <param name="name">The C# declared name.</param>
/// <param name="size">The native byte size, or null for an opaque marker.</param>
/// <param name="alignment">The required native allocation alignment.</param>
/// <param name="fields">Checked storage fields.</param>
public sealed record CSharpStoragePlan(
    StableId nativeType,
    string name,
    int? size,
    int? alignment,
    ImmutableArray<CSharpFieldPlan> fields
)
{
    /// <summary>Gets checked members forwarding the original value storage through an explicit binding context.</summary>
    public ImmutableArray<CSharpValueReceiverPlan> valueMembers { get; init; } = [];
    /// <summary>Gets explicit value-only conveniences independently of the carrier's physical layout.</summary>
    public CSharpRecordPresentationPlan? presentation { get; init; }
    /// <summary>Gets detached source documentation for this storage declaration.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets the inline array element count when this is an array carrier.</summary>
    public int? arrayLength { get; init; }
    /// <summary>Gets the inline array element carrier.</summary>
    public string? arrayElementType { get; init; }
    /// <summary>Gets the native pointer storage width when a fixed array requires integral pointer slots.</summary>
    public int? pointerArrayWidth { get; init; }
    /// <summary>Gets the enum integral carrier, or null for other storage.</summary>
    public string? enumUnderlyingType { get; init; }
    /// <summary>Gets checked enum names and integral constants.</summary>
    public ImmutableArray<CSharpEnumValuePlan> enumValues { get; init; } = [];
    /// <summary>Gets checked accessors for native flexible array tails without inventing an inline length.</summary>
    public ImmutableArray<CSharpFlexibleArrayPlan> flexibleArrays { get; init; } = [];
    /// <summary>Gets exact little-endian bit accessors over a checked complete native byte buffer.</summary>
    public ImmutableArray<CSharpBitfieldPlan> bitfields { get; init; } = [];
    /// <summary>Gets explicit-layout packing selected to preserve representable managed alignment; one remains the conservative storage fallback.</summary>
    public int packing { get; init; } = 1;
    /// <summary>Gets the planned managed alignment, or null when only addressable native storage is represented.</summary>
    public int? managedAlignment { get; init; }
    /// <summary>Gets the allocated internal alignment probe name, or null when no managed alignment is represented.</summary>
    public string? alignmentProbeName { get; init; }
}

/// <summary>Plans one integral bitfield without inventing a managed addressable native field.</summary>
/// <param name="name">The checked public property name.</param>
/// <param name="type">The checked integral or enum carrier.</param>
/// <param name="bitOffset">The absolute first bit in the native record.</param>
/// <param name="width">The native value width, from one through sixty-four.</param>
/// <param name="isSigned">Whether native reads sign-extend the stored value.</param>
public sealed record CSharpBitfieldPlan(
    string name,
    string type,
    long bitOffset,
    int width,
    bool isSigned
)
{
    /// <summary>Gets detached documentation for the original native bitfield.</summary>
    public string? documentation { get; init; }
}

/// <summary>Plans an ordinary unmanaged field at a proven native byte offset.</summary>
/// <param name="name">The checked managed field name.</param>
/// <param name="type">The checked managed storage type.</param>
/// <param name="offset">The exact native byte offset.</param>
public sealed record CSharpFieldPlan(
    string name,
    string type,
    int offset
)
{
    /// <summary>Gets the original field identity for checked external field-copy projections.</summary>
    public StableId? nativeField { get; init; }
    /// <summary>Gets detached documentation for the original native field.</summary>
    public string? documentation { get; init; }
}

/// <summary>Plans an enum value without injecting a native expression into C#.</summary>
/// <param name="name">The checked managed member name.</param>
/// <param name="value">The checked C# integral literal.</param>
public sealed record CSharpEnumValuePlan(
    string name,
    string value
)
{
    /// <summary>Gets optional UserAsserted managed value provenance without replacing native enum facts.</summary>
    public Evidence? presentationEvidence { get; init; }
    /// <summary>Gets detached documentation for the original native enumerator.</summary>
    public string? documentation { get; init; }
}

/// <summary>Plans a raw pointer accessor for an incomplete native array tail.</summary>
/// <param name="name">The checked accessor name.</param>
/// <param name="elementType">The native element's C# storage carrier.</param>
/// <param name="offset">The exact native tail byte offset.</param>
public sealed record CSharpFlexibleArrayPlan(
    string name,
    string elementType,
    int offset
)
{
    /// <summary>Gets detached documentation for the native flexible array tail.</summary>
    public string? documentation { get; init; }
}
