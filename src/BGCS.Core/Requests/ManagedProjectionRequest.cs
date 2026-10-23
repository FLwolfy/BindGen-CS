using System.Collections.Immutable;
using BGCS.Core.Managed;

namespace BGCS.Core.Requests;

/// <summary>Supplies immutable managed presentation choices to a projection.</summary>
/// <param name="namespace">The public managed namespace.</param>
/// <param name="name">The public API context name.</param>
public sealed record ManagedProjectionRequest(
    string @namespace,
    string name
)
{
    /// <summary>Gets whether canonical raw/native call signatures expose compiler-established native metadata.</summary>
    public bool emitNativeMetadata { get; init; }
    /// <summary>Gets exact existing native pointer shapes presented as explicitly non-owning addresses.</summary>
    public ImmutableArray<ManagedPointerCarrierRequest> pointerCarriers { get; init; } = [];
    /// <summary>Gets finite pointer-address callable surfaces retaining raw ABI and ownership diagnostics.</summary>
    public ImmutableArray<ManagedPointerFunctionRequest> pointerFunctions { get; init; } = [];
    /// <summary>Gets whether generated type declarations belong to the public API type.</summary>
    public bool nestGeneratedTypesInApi { get; init; }
    /// <summary>Gets whether missing source documentation receives a plain placeholder summary.</summary>
    public bool emitPlaceholderComments { get; init; }
    /// <summary>Gets whether selected callback typedefs expose standalone exact ABI delegate declarations.</summary>
    public bool emitCallbackDelegates { get; init; } = true;
    /// <summary>Gets whether public contexts expose their configured NativeLibraryName constant.</summary>
    public bool emitLibraryNameConstant { get; init; } = true;
    /// <summary>Gets an optional qualified consumer string constant whose frozen value must equal the resolved import library identity.</summary>
    public string? importLibraryNameConstant { get; init; }
    /// <summary>Gets exact source constant presentation rules.</summary>
    public ImmutableArray<ManagedConstantRuleRequest> constants { get; init; } = [];
    /// <summary>Gets existing native enum presentation rules, separate from additional managed enum definitions.</summary>
    public ImmutableArray<ManagedNativeEnumRuleRequest> nativeEnums { get; init; } = [];
    /// <summary>Gets category naming policies applied after exact symbol overrides are considered.</summary>
    public ManagedNamingPolicy naming { get; init; } = new();
    /// <summary>Gets exact callable documentation, names and constant-argument choices.</summary>
    public ImmutableArray<ManagedFunctionRuleRequest> functions { get; init; } = [];
    /// <summary>Gets optional exact record-carrier presentation rules.</summary>
    public ImmutableArray<ManagedRecordRuleRequest> records { get; init; } = [];
    /// <summary>Gets the explicit bound for automatic finite default overload expansion.</summary>
    public int defaultOverloadLimit { get; init; } = 128;
    /// <summary>Gets the requested context lifetime style.</summary>
    public ManagedApiStyle style { get; init; } = ManagedApiStyle.Instance;
    /// <summary>Gets explicitly declared native-to-managed name mappings.</summary>
    public ImmutableDictionary<string, string> names { get; init; } = ImmutableDictionary<string, string>.Empty;
    /// <summary>Gets explicitly declared fixed-width managed enums.</summary>
    public ImmutableArray<ManagedEnumDefinition> enums { get; init; } = [];
    /// <summary>Gets exact native scalar type names mapped to declared managed enum names.</summary>
    public ImmutableDictionary<string, string> enumMappings { get; init; } = ImmutableDictionary<string, string>.Empty;
    /// <summary>Gets explicit managed aliases in addition to compiler-established source aliases.</summary>
    public ImmutableArray<ManagedFunctionAliasRequest> functionAliases { get; init; } = [];
    /// <summary>Gets finite groups of existing checked operations.</summary>
    public ImmutableArray<ManagedFunctionContainerRequest> functionContainers { get; init; } = [];
    /// <summary>Gets explicitly selected C receiver/member projections.</summary>
    public ImmutableArray<ManagedReceiverRequest> memberFunctions { get; init; } = [];
}
