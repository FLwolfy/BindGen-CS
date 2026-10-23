using System.Collections.Immutable;

namespace BGCS.Core.Common;

/// <summary>Preserves parsed native documentation as text and semantic sections without parser nodes or XML syntax.</summary>
/// <param name="summary">Source prose, including unrecognized native documentation commands.</param>
/// <param name="parameters">Native parameter descriptions in source order.</param>
/// <param name="returns">The described native result, or null when absent.</param>
public sealed record SourceDocumentation(
    string summary,
    ImmutableArray<SourceParameterDocumentation> parameters,
    string? returns
)
{
    /// <summary>Gets template parameter descriptions, retained even when a concrete managed projection has no generic parameters.</summary>
    public ImmutableArray<SourceTypeParameterDocumentation> typeParameters { get; init; } = [];
}

/// <summary>Identifies a described source parameter independently of any managed rename or bridge channel.</summary>
/// <param name="name">The original native parameter name.</param>
/// <param name="index">The compiler-established source parameter index, or null when unresolved.</param>
/// <param name="text">The description as inert source text.</param>
public sealed record SourceParameterDocumentation(
    string name,
    int? index,
    string text
);

/// <summary>Retains a native template parameter description without claiming the emitted API is generic.</summary>
/// <param name="name">The original native template parameter name.</param>
/// <param name="text">The description as inert source text.</param>
public sealed record SourceTypeParameterDocumentation(
    string name,
    string text
);
