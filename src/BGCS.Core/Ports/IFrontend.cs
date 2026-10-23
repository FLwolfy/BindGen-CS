using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Requests;

namespace BGCS.Core.Ports;

/// <summary>Produces detached native facts from a resolved source request.</summary>
public interface IFrontend
{
    /// <summary>Interprets this frontend's input arguments and captures their complete external dependencies before cache lookup.</summary>
    /// <param name="request">The immutable source request, including explicitly selected target facts.</param>
    /// <param name="cancellationToken">Cancels bounded input preparation.</param>
    /// <returns>The exact parser request and its independently verifiable preparation inputs.</returns>
    ValueTask<FrontendPreparation> PrepareAsync(
        FrontendRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>Parses source inputs and releases all native parser resources before returning.</summary>
    /// <param name="request">Resolved source and target facts.</param>
    /// <param name="cancellationToken">Cancels the parse request.</param>
    /// <returns>A frozen graph and the independently checkable source snapshot.</returns>
    ValueTask<FrontendResult> ParseAsync(
        FrontendRequest request,
        CancellationToken cancellationToken
    );
}
