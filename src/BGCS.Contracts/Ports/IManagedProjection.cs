using System.Threading;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Projects a native ABI into managed API semantics without exposing backend syntax.</summary>
public interface IManagedProjection
{
    /// <summary>Builds a managed API model from verified native ABI facts.</summary>
    /// <param name="module">The verified native ABI module.</param>
    /// <param name="request">Immutable managed API choices.</param>
    /// <param name="cancellationToken">Cancels cooperative projection work.</param>
    /// <returns>The immutable managed API model.</returns>
    ManagedApiModel Project(
        InteropModule module,
        ManagedProjectionRequest request,
        CancellationToken cancellationToken
    );
}
