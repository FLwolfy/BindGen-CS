using System.Collections.Immutable;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;

namespace BGCS.Contracts.Requests;

/// <summary>Requests the selected native compiler's source view before frontend fact extraction.</summary>
/// <param name="source">Resolved original headers, source options and target toolchain.</param>
/// <param name="workingDirectory">A new caller-owned private directory for compiler preprocessing outputs.</param>
public sealed record NativeSourceViewRequest(
    FrontendRequest source,
    string workingDirectory
)
{
    /// <summary>Gets the complete preprocessing and dependency-discovery deadline.</summary>
    public int timeoutSeconds { get; init; } = 300;
}

/// <summary>Preserves one final native macro definition independently of frontend preprocessor behavior.</summary>
/// <param name="name">The exact native macro identifier.</param>
/// <param name="replacement">The final preprocessing-token replacement text.</param>
/// <param name="isFunctionLike">Whether a macro argument list is required.</param>
/// <param name="parameters">The ordered native macro parameter spellings.</param>
/// <param name="location">An unambiguous original definition, or null when the compiler dump cannot establish its origin.</param>
/// <param name="isSystem">Whether every established candidate is from the resolved SDK or compiler predefined environment.</param>
public sealed record NativeSourceMacro(
    string name,
    string replacement,
    bool isFunctionLike,
    ImmutableArray<string> parameters,
    SourceLocation? location,
    bool isSystem
);

/// <summary>Returns native-expanded source tokens and the actual compiler's final macro state.</summary>
/// <param name="expandedSource">Preprocessed source with comments and original source line metadata, without final macro dump directives.</param>
/// <param name="macros">Final active macro definitions, preserving unknown origins explicitly.</param>
/// <param name="inputs">Original source, headers, compiler bytes, arguments and search observations checked during execution.</param>
public sealed record NativeSourceViewResult(
    string expandedSource,
    ImmutableArray<NativeSourceMacro> macros,
    SourceSnapshot inputs
)
{
    /// <summary>Gets the actual native preprocessing command family, independently of the parser implementation.</summary>
    public string compilerFamily { get; init; } = "msvc";
    /// <summary>Gets the selected native driver's exact reported identity when available.</summary>
    public string compilerIdentity { get; init; } = string.Empty;
    /// <summary>Gets the expanded-source, final-macro, declared macro argv, diagnostic locale and compiler diagnostic identity with only private filename metadata and explicit Host-owned source origins mapped to portable identities.</summary>
    public ContentDigest observationFingerprint { get; init; }

    /// <summary>Gets actual native compiler diagnostics and declared or ambiguous macro provenance without inventing header positions.</summary>
    public ImmutableArray<BindingDiagnostic> diagnostics { get; init; } = [];
}
