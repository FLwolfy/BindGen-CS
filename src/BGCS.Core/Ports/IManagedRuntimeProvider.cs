using System.Diagnostics.CodeAnalysis;
using BGCS.Core.Targets;

namespace BGCS.Core.Ports;

/// <summary>Resolves an explicit execution model without core platform defaults.</summary>
public interface IManagedRuntimeProvider
{
    /// <summary>Resolves execution-model identities owned by this extension.</summary>
    /// <param name="request">The runtime identity and established target.</param>
    /// <param name="profile">The immutable profile on success.</param>
    /// <returns>False for an unowned identity; invalid owned requests fail explicitly.</returns>
    bool TryResolve(
        ManagedRuntimeRequest request,
        [NotNullWhen(true)] out ManagedRuntimeProfile? profile
    );
}
