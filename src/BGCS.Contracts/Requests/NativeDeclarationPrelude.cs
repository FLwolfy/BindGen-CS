using System.Collections.Immutable;

namespace BGCS.Contracts.Requests;

/// <summary>Declares an ordered dependency prelude whose declarations are loaded with isolated macros before frozen native source tokens.</summary>
/// <param name="source">Original dependency includes and established user macro state, in native include-entry order.</param>
/// <param name="headers">The actual authenticated absolute dependency entry headers referenced by the prelude.</param>
/// <remarks>This carries declarations, not an ABI equivalence claim. Native compiler proof is required before publishing the resulting graph.</remarks>
public sealed record NativeDeclarationPrelude(
    string source,
    ImmutableArray<string> headers
);
