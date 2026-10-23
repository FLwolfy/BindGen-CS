using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Requests;

namespace BGCS.Core.Ports;

/// <summary>Processes candidate source artifacts before final static contract checks.</summary>
public interface IArtifactProcessor
{
    /// <summary>Reads and replaces candidates through their owner without formal publication.</summary>
    /// <param name="request">The immutable projection and current candidate snapshot.</param>
    /// <param name="cancellationToken">Cancels cooperative processing.</param>
    /// <returns>The completed candidate processing operation.</returns>
    ValueTask ProcessAsync(
        ArtifactProcessingRequest request,
        CancellationToken cancellationToken
    );
}
