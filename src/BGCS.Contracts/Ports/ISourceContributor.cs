using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Produces immutable generated inputs before the original native parse and affected cache lookup.</summary>
public interface ISourceContributor
{
    /// <summary>Gets the globally unique declared contribution service identity.</summary>
    string id { get; }
    /// <summary>Gets descending execution priority, followed by ordinal service identity.</summary>
    int priority { get; }
    /// <summary>Returns complete source bytes while the Host retains path, execution, validation and publication ownership.</summary>
    /// <param name="request">The frozen source, target and presentation identities without candidate directories.</param>
    /// <param name="cancellationToken">Cancels cooperative source generation.</param>
    /// <returns>The immutable native, managed and resource contribution for this original generation target.</returns>
    ValueTask<SourceContribution> ContributeAsync(
        SourceContributionRequest request,
        CancellationToken cancellationToken
    );
}
