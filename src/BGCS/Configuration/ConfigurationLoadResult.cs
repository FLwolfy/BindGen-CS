using System.Collections.Immutable;
using BGCS.Contracts.Inputs;

namespace BGCS.Configuration;

/// <summary>Preserves effective configuration and every local document used during composition.</summary>
/// <param name="configuration">The validated immutable effective configuration.</param>
/// <param name="inputs">Exact input document identities and contents.</param>
public sealed record ConfigurationLoadResult(
    BindingConfiguration configuration,
    ImmutableArray<InputDependency> inputs
)
{
}
