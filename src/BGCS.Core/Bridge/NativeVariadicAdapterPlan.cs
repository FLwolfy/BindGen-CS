using System.Collections.Immutable;
using BGCS.Core.Common;
using BGCS.Core.Requests;

namespace BGCS.Core.Bridge;

/// <summary>Maps an original variadic declaration to one finite native wrapper that must pass the ordinary reparse and bridge checks.</summary>
/// <param name="sourceDeclaration">The original variadic function retained for inspection and native compiler proof.</param>
/// <param name="sourceSignature">The exact original overload identity.</param>
/// <param name="wrapper">The concrete generated wrapper's qualified native name.</param>
/// <param name="fixedParameterCount">The unchanged original fixed native prefix length.</param>
/// <param name="additionalArguments">The explicitly requested finite additional native arguments.</param>
public sealed record NativeVariadicAdapter(
    StableId sourceDeclaration,
    string sourceSignature,
    string wrapper,
    int fixedParameterCount,
    ImmutableArray<VariadicArgumentRequest> additionalArguments
)
{
    /// <summary>Gets actual frontend-established additional argument types from the original parse's explicit probes.</summary>
    public ImmutableArray<StableId> argumentTypes { get; init; } = [];
}

/// <summary>Owns the exact controlled native adapter declarations and their detached source mappings.</summary>
/// <param name="source">The native source appended after the selected original headers.</param>
/// <param name="adapters">The finite original-to-wrapper mapping; this is not an arbitrary managed varargs calling mechanism.</param>
public sealed record NativeVariadicAdapterPlan(
    ControlledSourceFragment source,
    ImmutableArray<NativeVariadicAdapter> adapters
);
