using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Plans and emits managed source artifacts from immutable ABI and managed API semantics.</summary>
public interface IBindingBackend
{
    /// <summary>Emits complete candidate artifacts without publishing them.</summary>
    /// <param name="model">The frozen managed API and native ABI.</param>
    /// <param name="request">Candidate storage and backend choices.</param>
    /// <param name="cancellationToken">Cancels cooperative emission.</param>
    /// <returns>The complete emitted artifact descriptors.</returns>
    ValueTask<ImmutableArray<ArtifactDescriptor>> EmitAsync(
        ManagedApiModel model,
        EmissionRequest request,
        CancellationToken cancellationToken
    );
}
