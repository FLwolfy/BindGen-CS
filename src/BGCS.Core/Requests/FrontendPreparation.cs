using System.Collections.Immutable;
using BGCS.Core.Inputs;

namespace BGCS.Core.Requests;

/// <summary>Freezes frontend-owned argument interpretation before source parsing and cache lookup.</summary>
/// <param name="request">The exact detached request to pass to the parser.</param>
/// <param name="inputs">Every external file and missing search input read while preparing the request.</param>
/// <param name="cacheIneligibilityReasons">Explicit reasons why the frontend cannot establish a complete input identity for reusing the parse stage.</param>
public sealed record FrontendPreparation(
    FrontendRequest request,
    SourceSnapshot inputs,
    ImmutableArray<string> cacheIneligibilityReasons
);
