using System.Collections.Immutable;

namespace BGCS.Contracts.Bridge;

/// <summary>Requests one concrete class-template specialization in the native parse unit.</summary>
/// <param name="id">The unique user-facing selection identity.</param>
/// <param name="nativeType">The fully qualified concrete source-language specialization.</param>
public sealed record TypeTemplateInstantiation(
    string id,
    string nativeType
);

/// <summary>Requests one concrete function-template call through a generated fixed native prototype.</summary>
/// <param name="id">The unique user-facing selection identity.</param>
/// <param name="nativeCallable">The fully qualified callable with explicit template arguments.</param>
/// <param name="returnType">The concrete native return type.</param>
/// <param name="parameterTypes">The exact concrete native parameter types in source order.</param>
public sealed record FunctionTemplateInstantiation(
    string id,
    string nativeCallable,
    string returnType,
    ImmutableArray<string> parameterTypes
);

/// <summary>Owns explicit template selections before parsing; no frontend AST or mutable configuration is retained.</summary>
public sealed record TemplateInstantiationRequest
{
    /// <summary>Gets explicitly requested concrete class-template specializations.</summary>
    public ImmutableArray<TypeTemplateInstantiation> types { get; init; } = [];
    /// <summary>Gets explicitly requested fixed function-template wrappers.</summary>
    public ImmutableArray<FunctionTemplateInstantiation> functions { get; init; } = [];
}

/// <summary>Describes the finite declarations added after original headers for explicit template selections.</summary>
/// <param name="postamble">The validated native parse declarations.</param>
/// <param name="functionWrappers">The fixed wrapper qualified names by user-facing identity.</param>
/// <param name="concreteTypes">The requested concrete source-language type names.</param>
public sealed record TemplateInstantiationPlan(
    string postamble,
    ImmutableDictionary<string, string> functionWrappers,
    ImmutableArray<string> concreteTypes
);
