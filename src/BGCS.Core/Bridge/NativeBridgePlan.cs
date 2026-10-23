using System.Collections.Immutable;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Managed;

namespace BGCS.Core.Bridge;

/// <summary>Identifies a C representation independently of a parser or managed carrier.</summary>
public enum BridgeTypeKind
{
    /// <summary>An established C type declared by one of the explicit public input headers.</summary>
    NativeStorage,
    /// <summary>A fixed-width scalar with a checked native conversion.</summary>
    Scalar,
    /// <summary>A pointer to another bridge type.</summary>
    Pointer,
    /// <summary>An opaque source-language object identity.</summary>
    Handle,
    /// <summary>A named fixed-width C typedef preserving a source enumeration identity.</summary>
    Enumeration,
    /// <summary>A compiler-established C++ DTO storage contract exposed through C pointer channels.</summary>
    Record,
    /// <summary>A fixed inline array whose exact elements belong to the checked storage graph.</summary>
    FixedArray,
    /// <summary>A typed C function signature, used through pointers and independently reparsed before import emission.</summary>
    Function
}

/// <summary>Describes a C storage shape and its source-language origin.</summary>
/// <param name="id">The stable bridge type identity.</param>
/// <param name="kind">The C representation category.</param>
/// <param name="cName">The allocated C type name or fixed-width scalar spelling.</param>
public sealed record BridgeType(
    StableId id,
    BridgeTypeKind kind,
    string cName
)
{
    /// <summary>Gets the source-language type represented by this shape.</summary>
    public StableId? nativeType { get; init; }
    /// <summary>Gets the bridge-owned generated identity when no original native type exists.</summary>
    public StableId? generatedType { get; init; }
    /// <summary>Gets the pointee identity.</summary>
    public StableId? elementType { get; init; }
    /// <summary>Gets whether a pointer refers to const storage.</summary>
    public bool isConst { get; init; }
    /// <summary>Gets the complete checked DTO storage contract when Record is selected.</summary>
    public BridgeRecord? recordStorage { get; init; }
    /// <summary>Gets the positive inline element count when FixedArray is selected.</summary>
    public long? arrayLength { get; init; }
    /// <summary>Gets the exact registered C result carrier when Function is selected.</summary>
    public StableId? returnType { get; init; }
    /// <summary>Gets the exact registered C parameter carriers when Function is selected.</summary>
    public ImmutableArray<StableId> parameterTypes { get; init; } = [];
    /// <summary>Gets the native callback calling convention without guessing it from pointer width.</summary>
    public string? callingConvention { get; init; }
    /// <summary>Gets whether a function signature has native variadic trailing arguments.</summary>
    public bool isVariadic { get; init; }
    /// <summary>Gets whether a function signature has a complete native prototype.</summary>
    public bool hasPrototype { get; init; } = true;
}

/// <summary>Identifies the source-language action performed inside an exception boundary.</summary>
public enum BridgeOperationKind
{
    /// <summary>Calls a qualified free function.</summary>
    FreeCall,
    /// <summary>Calls a qualified static member.</summary>
    StaticCall,
    /// <summary>Calls a member on a checked opaque instance.</summary>
    InstanceCall,
    /// <summary>Constructs an owned native object.</summary>
    Construct,
    /// <summary>Deletes a native object through its declared type.</summary>
    Destroy,
    /// <summary>Performs a compiler-checked native base-class conversion.</summary>
    BaseCast,
    /// <summary>Performs a runtime-checked native conversion from a polymorphic base to a derived class.</summary>
    CheckedDerivedCast,
    /// <summary>Performs a checked operation on an opaque standard-library value.</summary>
    StandardValue,
    /// <summary>Performs compiler-established default construction without inventing a source callable declaration.</summary>
    DefaultConstruct,
    /// <summary>Invokes an explicitly typed finite wrapper for an original variadic declaration.</summary>
    FiniteVariadicCall,
    /// <summary>Constructs a planned generated virtual proxy without inventing source declaration facts.</summary>
    VirtualProxyConstruct,
    /// <summary>Destroys the exact generated virtual proxy type.</summary>
    VirtualProxyDestroy,
    /// <summary>Performs the planned generated proxy's native interface adjustment.</summary>
    VirtualProxyCast
}

/// <summary>Identifies a structured argument or result conversion.</summary>
public enum BridgeConversionKind
{
    /// <summary>Uses a native static_cast between checked scalar shapes.</summary>
    ScalarCast,
    /// <summary>Converts a borrowed pointer through an opaque C representation.</summary>
    PointerCast,
    /// <summary>Dereferences a checked borrowed pointer for a native reference or value.</summary>
    Dereference,
    /// <summary>Moves a native rvalue reference or unique-pointer payload; its C holder remains caller-owned.</summary>
    Move,
    /// <summary>Takes the address of a native lvalue result.</summary>
    ReferenceAddress,
    /// <summary>Allocates an owned copy of a native value result.</summary>
    OwnedValue,
    /// <summary>Uses an explicitly declared and fingerprinted native expression.</summary>
    SourceExpression,
    /// <summary>Copies a checked trivially copyable DTO through a C pointer or result-out channel.</summary>
    RecordCopy
}

/// <summary>Describes one C parameter and the exact source-language conversion.</summary>
/// <param name="id">The stable C parameter identity.</param>
/// <param name="name">The allocated C identifier.</param>
/// <param name="type">The C representation identity.</param>
/// <param name="nativeType">The original native parameter type.</param>
/// <param name="conversion">The checked conversion performed before invoking native code.</param>
public sealed record BridgeParameter(
    StableId id,
    string name,
    StableId type,
    StableId? nativeType,
    BridgeConversionKind conversion
)
{
    /// <summary>Gets the bridge-owned type for a generated object parameter without an original native declaration.</summary>
    public StableId? generatedType { get; init; }
    /// <summary>Gets the explicit input expression when SourceExpression is selected.</summary>
    public ControlledSourceFragment? conversionExpression { get; init; }
    /// <summary>Gets explicit managed conversion assertions for the native input value.</summary>
    public ManagedRecipeProjection? managedProjection { get; init; }
}

/// <summary>Stores a callable action without source fragments, services or parser handles.</summary>
/// <param name="kind">The native action category.</param>
/// <param name="declaration">The originating native callable, or null for a generated conversion.</param>
/// <param name="ownerType">The owning native class, or null for a free function.</param>
/// <param name="instanceParameter">The borrowed C instance parameter, when required.</param>
public sealed record BridgeOperation(
    BridgeOperationKind kind,
    StableId? declaration,
    StableId? ownerType,
    StableId? instanceParameter
)
{
    /// <summary>Gets the exact finite variadic wrapper contract when FiniteVariadicCall is selected.</summary>
    public NativeVariadicAdapter? finiteVariadic { get; init; }
    /// <summary>Gets the typed virtual proxy identity for a generated construction, destruction or interface cast.</summary>
    public string? virtualProxyId { get; init; }
    /// <summary>Gets the destination native type for a base-class conversion.</summary>
    public StableId? castTarget { get; init; }
    /// <summary>Gets the typed standard-library operation; no source expressions are stored here.</summary>
    public StandardValueOperation? standardValue { get; init; }
    /// <summary>Gets an explicit wrapper expression around the independently planned native invocation.</summary>
    public ControlledSourceFragment? invocationExpression { get; init; }
    /// <summary>Gets exact native argument groups when multiple C carriers represent an original parameter.</summary>
    public ImmutableArray<BridgeArgumentGroup> argumentGroups { get; init; } = [];
}

/// <summary>Describes a status-returning C export, including its independent original result.</summary>
/// <param name="id">The stable bridge export identity.</param>
/// <param name="name">The allocated C export symbol.</param>
/// <param name="parameters">The immutable C input parameters, including an explicit instance where required.</param>
/// <param name="operation">The typed source-language action.</param>
public sealed record BridgeExport(
    StableId id,
    string name,
    ImmutableArray<BridgeParameter> parameters,
    BridgeOperation operation
)
{
    /// <summary>Gets whether the original result uses a separate output pointer rather than a physical return value.</summary>
    public bool resultInOutputParameter { get; init; } = true;
    /// <summary>Gets whether this operation uses the structured status and error protocol.</summary>
    public bool usesErrorChannel { get; init; } = true;
    /// <summary>Gets the preserved primary export when this is an explicitly requested input adapter.</summary>
    public StableId? inputAdapterOf { get; init; }
    /// <summary>Gets the C representation of the original return value; null denotes a void operation.</summary>
    public StableId? resultType { get; init; }
    /// <summary>Gets the original native return type where applicable.</summary>
    public StableId? nativeResultType { get; init; }
    /// <summary>Gets the result conversion.</summary>
    public BridgeConversionKind resultConversion { get; init; }
    /// <summary>Gets the paired release export for a newly owned result.</summary>
    public StableId? resultRelease { get; init; }
    /// <summary>Gets explicit allocator and release assertions for custom owned C pointer results.</summary>
    public BridgeResultOwnership? resultOwnership { get; init; }
    /// <summary>Gets detached native documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets whether a generated pointer result borrows storage from its explicit instance.</summary>
    public bool resultBorrowsInstance { get; init; }
    /// <summary>Gets the explicit output expression when SourceExpression is selected.</summary>
    public ControlledSourceFragment? resultExpression { get; init; }
    /// <summary>Gets explicit managed conversion assertions for the original native return value.</summary>
    public ManagedRecipeProjection? managedResultProjection { get; init; }
}

/// <summary>Owns a typed C++ lowering plan with explicit exception and release boundaries.</summary>
/// <param name="source">The detached source-language graph.</param>
/// <param name="exportPrefix">The checked C export prefix.</param>
/// <param name="headers">The ordered logical native header inputs.</param>
/// <param name="types">The immutable C representation graph.</param>
/// <param name="exports">The immutable native operation plans.</param>
public sealed record NativeBridgePlan(
    NativeModule source,
    string exportPrefix,
    ImmutableArray<string> headers,
    ImmutableArray<BridgeType> types,
    ImmutableArray<BridgeExport> exports
)
{
    /// <summary>Gets explicitly associated C callback thunks whose managed boundary uses pointer carriers.</summary>
    public ImmutableArray<AggregateCallbackAdapter> aggregateCallbacks { get; init; } = [];
    /// <summary>Gets owned callback contexts released only after an explicit native stop-and-join completes.</summary>
    public ImmutableArray<NativeCallbackContextPlan> callbackContexts { get; init; } = [];
    /// <summary>Gets the explicitly supplied compiler spelling for native export and C calling conventions.</summary>
    public BGCS.Core.Targets.NativeSourceConventions? sourceConventions { get; init; }
    /// <summary>Gets original C semantics that must survive physical pointer-carrier normalization.</summary>
    public BGCS.Core.Interop.InteropModule? sourceSemantics { get; init; }
    /// <summary>Gets the locked bridge model revision.</summary>
    public int modelRevision => BGCS.Core.Versioning.ModelRevision.C_NATIVE_BRIDGE;
    /// <summary>Gets explicit source text placed before the original native headers.</summary>
    public string preamble { get; init; } = string.Empty;
    /// <summary>Gets explicit source text placed after the original native headers.</summary>
    public string postamble { get; init; } = string.Empty;
    /// <summary>Gets fingerprinted native helper declarations supplied by lowering recipes or plugins.</summary>
    public ImmutableArray<ControlledSourceFragment> sourceFragments { get; init; } = [];
    /// <summary>Gets the generated virtual proxy contracts, including their exact raw callback slots.</summary>
    public ImmutableArray<CppVirtualProxyPlan> virtualProxies { get; init; } = [];
    /// <summary>Gets exact user-asserted family bindings independently checked against the original native graph and final compiler operations.</summary>
    public ImmutableArray<CppFamilyBinding> familyBindings { get; init; } = [];
    /// <summary>Gets immutable minimum dialect requirements derived from the actual typed operations.</summary>
    public ImmutableArray<CppLanguageRequirement> languageRequirements
    {
        get
        {
            if (source.language == BGCS.Core.Inputs.SourceLanguage.C) return [];
            ImmutableArray<CppLanguageRequirement>.Builder requirements = ImmutableArray.CreateBuilder<CppLanguageRequirement>();
            requirements.Add(new("bridge.exception-boundary", 11));
            bool recordCopy = false, standard17 = false, standard20 = false, standard23 = false;
            foreach (BridgeExport export in exports)
            {
                recordCopy |= export.resultConversion == BridgeConversionKind.RecordCopy;
                foreach (BridgeParameter parameter in export.parameters) recordCopy |= parameter.conversion == BridgeConversionKind.RecordCopy;
                if (export.operation.standardValue is StandardValueOperation standard) Require(standard.family);
                foreach (BridgeArgumentGroup group in export.operation.argumentGroups)
                    if (group.standardSequence is StandardSequenceArgument sequence)
                    {
                        Require(sequence.family);
                        standard20 |= sequence.family == StandardValueKind.Path;
                    }
            }
            if (recordCopy) requirements.Add(new("bridge.dto-copy", 20));
            if (standard17) requirements.Add(new("bridge.standard-value-cxx17", 17));
            if (standard20) requirements.Add(new("bridge.standard-value-cxx20", 20));
            if (standard23) requirements.Add(new("bridge.standard-value-cxx23", 23));
            return requirements.ToImmutable();

            void Require(StandardValueKind family)
            {
                standard17 |= family is StandardValueKind.Optional or StandardValueKind.Variant or StandardValueKind.Path;
                standard20 |= family == StandardValueKind.Span;
                standard23 |= family == StandardValueKind.Expected;
            }
        }
    }
    /// <summary>Gets the generated opaque error type name.</summary>
    public string errorTypeName => exportPrefix.TrimEnd('_') + "_error";
    /// <summary>Gets the exact error-message export.</summary>
    public string errorMessageExport => exportPrefix.TrimEnd('_') + "_error_message";
    /// <summary>Gets the exact error-release export.</summary>
    public string errorReleaseExport => exportPrefix.TrimEnd('_') + "_error_release";
}
