using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Artifacts;
using BGCS.Core.Managed;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp;

/// <summary>Plans and emits candidate C# sources from neutral semantic models.</summary>
public sealed class CSharpBackend : IBindingBackend
{
    /// <summary>Checks the complete model and emission choices without writing candidate artifacts.</summary>
    /// <param name="model">The immutable managed projection and its native ABI.</param>
    /// <param name="request">The exact emission choices; its sink is not used during preparation.</param>
    /// <param name="cancellationToken">Cancels cooperative source planning.</param>
    /// <returns>A prepared emission that can only substitute its output sink.</returns>
    public CSharpEmission Prepare(
        ManagedApiModel model,
        EmissionRequest request,
        CancellationToken cancellationToken = default
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request, cancellationToken);
        if (!Enum.IsDefined(request.importMode)) throw new ArgumentOutOfRangeException(nameof(request));
        return new(plan, request);
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableArray<ArtifactDescriptor>> EmitAsync(
        ManagedApiModel model,
        EmissionRequest request,
        CancellationToken cancellationToken
    ) => Prepare(model, request, cancellationToken).EmitAsync(request.sink, cancellationToken);
}
