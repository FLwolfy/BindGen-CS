using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Processes candidate source artifacts before final static contract checks.</summary>
public interface IArtifactProcessor
{
    /// <summary>Gets the globally unique processor identity.</summary>
    string id { get; }
    /// <summary>Gets descending execution priority, followed by ordinal identity.</summary>
    int priority { get; }
    /// <summary>Reads and replaces candidates through their owner without formal publication.</summary>
    /// <param name="request">The immutable projection and current candidate snapshot.</param>
    /// <param name="cancellationToken">Cancels cooperative processing.</param>
    /// <returns>The completed candidate processing operation.</returns>
    ValueTask ProcessAsync(
        ArtifactProcessingRequest request,
        CancellationToken cancellationToken
    );
}
