using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Produces detached native facts from a resolved source request.</summary>
public interface IFrontend
{
    /// <summary>Parses source inputs and releases all native parser resources before returning.</summary>
    /// <param name="request">Resolved source and target facts.</param>
    /// <param name="cancellationToken">Cancels the parse request.</param>
    /// <returns>A frozen graph and the independently checkable source snapshot.</returns>
    ValueTask<FrontendResult> ParseAsync(
        FrontendRequest request,
        CancellationToken cancellationToken
    );
}
