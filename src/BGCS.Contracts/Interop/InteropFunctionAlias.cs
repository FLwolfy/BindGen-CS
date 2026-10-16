using BGCS.Contracts.Common;

namespace BGCS.Contracts.Interop;

/// <summary>Names an existing checked native function through an object-like source macro without inventing another export.</summary>
/// <param name="sourceDeclaration">The originating macro identity.</param>
/// <param name="name">The source alias name.</param>
/// <param name="qualifiedName">The exact source alias selector.</param>
/// <param name="nativeFunction">The existing final interoperability function identity.</param>
/// <param name="evidence">The compiler-derived alias provenance.</param>
public sealed record InteropFunctionAlias(
    StableId sourceDeclaration,
    string name,
    string qualifiedName,
    StableId nativeFunction,
    Evidence evidence
)
{
    /// <summary>Gets the original alias source position.</summary>
    public SourceLocation? source { get; init; }
    /// <summary>Gets detached alias documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets structured inert alias documentation when available.</summary>
    public SourceDocumentation? documentationContent { get; init; }
}
