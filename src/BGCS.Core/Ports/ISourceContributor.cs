using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Artifacts;
using BGCS.Core.Requests;

namespace BGCS.Core.Ports;

/// <summary>Produces immutable generated inputs before the original native parse and affected cache lookup.</summary>
public interface ISourceContributor
{
    /// <summary>Returns complete source bytes while the Host retains path, execution, validation and publication ownership.</summary>
    /// <param name="request">The frozen source, target and presentation identities without candidate directories.</param>
    /// <param name="cancellationToken">Cancels cooperative source generation.</param>
    /// <returns>The immutable native, managed and resource contribution for this original generation target.</returns>
    ValueTask<SourceContribution> ContributeAsync(
        SourceContributionRequest request,
        CancellationToken cancellationToken
    );
}
