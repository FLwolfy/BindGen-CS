using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate;

namespace BGCS.Cpp2C.Adapters;

/// <summary>
/// Version of the public C++ adapter service-provider contract.
/// </summary>
public static class CppAdapterContract
{
    public const int CurrentVersion = 1;
}

/// <summary>Location in which a C++ type is being lowered.</summary>
public enum CppTypeAdapterUse
{
    Field,
    Parameter,
    Return
}

/// <summary>Semantic category exposed to analyzers and bridge emitters.</summary>
public enum CppTypeAdapterKind
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

/// <summary>Read-only context passed to a type adapter.</summary>
public sealed record CppTypeAdapterContext(Cpp2CGeneratorConfig Configuration, CppTypeAdapterUse Use);

/// <summary>Stable result returned by a C++ type adapter.</summary>
public sealed record CppTypeAdapterPlan(
    string AdapterName,
    CppTypeAdapterKind Kind,
    string CAbiType,
    MarshallingStrategy Marshalling,
    BindingOwnership Ownership,
    bool RequiresCleanup = false,
    string? CleanupFunction = null);

/// <summary>
/// Extends bridge type recognition without modifying the generator. Adapters must be deterministic and thread-safe.
/// </summary>
public interface ICppTypeAdapter
{
    string Name { get; }
    int Priority { get; }
    bool CanAdapt(CppType type, CppTypeAdapterContext context);
    CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context);
}

/// <summary>Read-only context passed to callable adapters.</summary>
public sealed record CppCallableAdapterContext(
    Cpp2CGeneratorConfig Configuration,
    CppClass? DeclaringType,
    string DefaultExportName);

/// <summary>Stable callable lowering decision.</summary>
public sealed record CppCallableAdapterPlan(string AdapterName, string ExportName, bool Exclude = false);

/// <summary>
/// Extends callable selection and exported naming without modifying the generator.
/// </summary>
public interface ICppCallableAdapter
{
    string Name { get; }
    int Priority { get; }
    bool CanAdapt(CppFunction function, CppCallableAdapterContext context);
    CppCallableAdapterPlan CreatePlan(CppFunction function, CppCallableAdapterContext context);
}
