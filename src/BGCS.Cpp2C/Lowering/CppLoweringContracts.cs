using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate;

namespace BGCS.Cpp2C.Lowering;

/// <summary>Location in which a C++ type is being lowered.</summary>
public enum CppTypeLoweringUse
{
    Field,
    Parameter,
    Return
}

/// <summary>Semantic category exposed to analyzers, bridge emitters, and managed projections.</summary>
public enum CppTypeLoweringKind
{
    Custom,
    Utf8String,
    UniqueOwner,
    SharedOwner,
    Span,
    Vector,
    Optional,
    Array,
    Map,
    Set,
    Variant,
    Expected,
    Path,
    ChronoDuration,
    ChronoTimePoint
}

/// <summary>Shape of the stable C ABI produced for one C++ value.</summary>
public enum CppAbiShape
{
    Direct,
    PointerAndLength,
    OptionalValue,
    OpaqueHandle,
    Custom
}

/// <summary>Evidence level attached to extension-supplied code and conversion rules.</summary>
public enum CppLoweringSafety
{
    Verified,
    UserAsserted,
    Unsafe
}

/// <summary>Controls which extension-supplied lowering evidence the generator accepts.</summary>
public enum CppLoweringSafetyPolicy
{
    VerifiedOnly,
    AllowUserAsserted,
    AllowUnsafe
}

/// <summary>One C ABI parameter contributed by a type lowering.</summary>
/// <param name="NameSuffix">Suffix appended to the native parameter name, or an empty string for the primary value.</param>
/// <param name="CAbiType">C-compatible parameter type.</param>
public sealed record CppAbiParameter(string NameSuffix, string CAbiType);

/// <summary>Optional managed projection metadata consumed by managed artifact contributors and tooling.</summary>
public sealed record CppManagedProjection(
    string ManagedType,
    string? ManagedToNativeExpression = null,
    string? NativeToManagedExpression = null,
    string? RequiredNamespace = null);

/// <summary>Read-only context passed to a type lowering.</summary>
public sealed record CppTypeLoweringContext(Cpp2CGeneratorConfig Configuration, CppTypeLoweringUse Use);

/// <summary>
/// Complete lowering decision for a C++ type. Conversion expressions use the placeholders
/// <c>{value}</c>, <c>{name}</c>, <c>{count}</c>, and <c>{cppType}</c>.
/// </summary>
public sealed record CppTypeLoweringPlan(
    string LoweringName,
    CppTypeLoweringKind Kind,
    string CAbiType,
    MarshallingStrategy Marshalling,
    BindingOwnership Ownership)
{
    public CppAbiShape AbiShape { get; init; } = CppAbiShape.Direct;
    public IReadOnlyList<CppAbiParameter> AbiParameters { get; init; } = [];
    public string? ParameterToCppExpression { get; init; }
    public string? ReturnToCExpression { get; init; }
    public bool RequiresCleanup { get; init; }
    public string? CleanupFunction { get; init; }
    public BindingAllocatorKind AllocatorKind { get; init; } = BindingAllocatorKind.Unspecified;
    public string? AllocatorFunction { get; init; }
    public CppManagedProjection? ManagedProjection { get; init; }
    public IReadOnlyList<string> RequiredHeaders { get; init; } = [];
    public CppLoweringSafety Safety { get; init; } = CppLoweringSafety.Verified;
}

/// <summary>Extends bridge type lowering without modifying the generator.</summary>
public interface ICppTypeLowering
{
    string Name { get; }
    int Priority { get; }
    bool CanLower(CppType type, CppTypeLoweringContext context);
    CppTypeLoweringPlan CreatePlan(CppType type, CppTypeLoweringContext context);
}

/// <summary>Read-only context passed to callable lowerings.</summary>
public sealed record CppCallableLoweringContext(
    Cpp2CGeneratorConfig Configuration,
    CppClass? DeclaringType,
    string DefaultExportName);

/// <summary>
/// Callable-level lowering decision. <see cref="InvocationExpression"/> can wrap or replace the default
/// invocation using <c>{invocation}</c>; the generated expression must return the native callable result.
/// </summary>
public sealed record CppCallableLoweringPlan(string LoweringName, string ExportName)
{
    public bool Exclude { get; init; }
    public string? InvocationExpression { get; init; }
    public IReadOnlyList<string> RequiredHeaders { get; init; } = [];
    public CppLoweringSafety Safety { get; init; } = CppLoweringSafety.Verified;
}

/// <summary>Extends callable selection, exported naming, and invocation lowering.</summary>
public interface ICppCallableLowering
{
    string Name { get; }
    int Priority { get; }
    bool CanLower(CppFunction function, CppCallableLoweringContext context);
    CppCallableLoweringPlan CreatePlan(CppFunction function, CppCallableLoweringContext context);
}

/// <summary>Destination for a generated extension artifact.</summary>
public enum CppGeneratedArtifactKind
{
    PublicHeader,
    NativeSource,
    ManagedSource,
    Resource
}

/// <summary>An immutable source or resource contributed by a lowering plugin.</summary>
public sealed record CppGeneratedArtifact(
    string RelativePath,
    string Content,
    CppGeneratedArtifactKind Kind,
    bool ExposeToBindings = false,
    CppLoweringSafety Safety = CppLoweringSafety.Verified);

/// <summary>Context supplied when a plugin contributes bridge or managed artifacts.</summary>
public sealed record CppArtifactContext(
    Cpp2CGeneratorConfig Configuration,
    string TargetIdentifier,
    CppGeneratedArtifactKind Stage);

/// <summary>Contributes deterministic native or managed artifacts to the generated package.</summary>
public interface ICppArtifactContributor
{
    string Name { get; }
    int Priority { get; }
    IReadOnlyList<CppGeneratedArtifact> Contribute(CppArtifactContext context);
}
