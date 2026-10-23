using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Bridge;

/// <summary>Names one exact C carrier used to construct an original native argument.</summary>
/// <param name="name">The expression placeholder identity local to this argument.</param>
/// <param name="parameter">The exact exported C parameter identity.</param>
public sealed record BridgeArgumentBinding(
    string name,
    StableId parameter
);

/// <summary>Maps a finite set of C carriers to one original native parameter without changing callable identity.</summary>
/// <param name="sourceParameter">The exact original native parameter declaration identity.</param>
/// <param name="nativeType">The unchanged original native parameter type identity.</param>
/// <param name="inputs">Ordered named C carriers, each consumed once in the full argument plan.</param>
/// <param name="expression">The fingerprinted conversion expression, or null for an ordinary single-carrier conversion.</param>
public sealed record BridgeArgumentGroup(
    StableId sourceParameter,
    StableId nativeType,
    ImmutableArray<BridgeArgumentBinding> inputs,
    ControlledSourceFragment? expression
)
{
    /// <summary>Gets a checked finite sequence construction instead of an arbitrary conversion expression.</summary>
    public StandardSequenceArgument? standardSequence { get; init; }
}

/// <summary>Declares one named fixed C input in a configured native value recipe.</summary>
/// <param name="name">The local native expression placeholder.</param>
/// <param name="cAbiType">The fixed scalar or one-level pointer carrier spelling.</param>
public sealed record RecipeAbiParameter(
    string name,
    string cAbiType
);

/// <summary>Stores a resolved input carrier without parser or backend dependencies.</summary>
/// <param name="name">The local expression placeholder.</param>
/// <param name="type">The resolved C representation identity in the type lowering graph.</param>
public sealed record CppAbiInput(
    string name,
    StableId type
);
