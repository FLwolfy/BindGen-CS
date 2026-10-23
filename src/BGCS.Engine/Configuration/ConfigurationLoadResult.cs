using BGCS.Core.Inputs;

namespace BGCS.Configuration;

/// <summary>Preserves effective configuration and every local document used during composition.</summary>
/// <param name="configuration">The validated immutable effective configuration.</param>
/// <param name="snapshot">Exact input document identities and contents.</param>
public sealed record ConfigurationLoadResult(
    BindingConfiguration configuration,
    SourceSnapshot snapshot
)
{
}
