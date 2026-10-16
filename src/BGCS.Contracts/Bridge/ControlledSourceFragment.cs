using System.Collections.Immutable;
using BGCS.Contracts.Common;

namespace BGCS.Contracts.Bridge;

/// <summary>Identifies the explicit native source escape hatch's placement and substitution rules.</summary>
public enum SourceFragmentKind
{
    /// <summary>A single source-language expression using declared placeholders.</summary>
    Expression,
    /// <summary>Declarations and helper definitions included after the original headers.</summary>
    TranslationUnit
}

/// <summary>Owns explicit source text with its origin, content identity, dependencies and asserted evidence.</summary>
/// <param name="origin">The plugin or recipe identity responsible for the source.</param>
/// <param name="kind">The native source placement.</param>
/// <param name="text">The complete source text, without services or native handles.</param>
/// <param name="digest">The SHA-256 identity of the exact UTF-8 source text.</param>
public sealed record ControlledSourceFragment(
    string origin,
    SourceFragmentKind kind,
    string text,
    ContentDigest digest
)
{
    /// <summary>Gets explicitly referenced source declarations.</summary>
    public ImmutableArray<StableId> declarationDependencies { get; init; } = [];
    /// <summary>Gets explicitly referenced source types.</summary>
    public ImmutableArray<StableId> typeDependencies { get; init; } = [];
    /// <summary>Gets headers already declared as parse and native build inputs.</summary>
    public ImmutableArray<string> requiredHeaders { get; init; } = [];
    /// <summary>Gets the source's semantic assertion, which is never promoted into independently verified evidence.</summary>
    public Evidence evidence { get; init; } = new(EvidenceLevel.UserAsserted, "native-source-fragment");
}
