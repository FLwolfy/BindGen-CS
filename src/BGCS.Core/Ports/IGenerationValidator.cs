using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Requests;

namespace BGCS.Core.Ports;

/// <summary>Checks final candidate contracts and artifacts without owning formal publication.</summary>
public interface IGenerationValidator
{
    /// <summary>Validates one target's complete candidate after source generation and processing.</summary>
    /// <param name="request">The immutable target, ABI and verified artifact read surface.</param>
    /// <param name="cancellationToken">Cancels cooperative validation.</param>
    /// <returns>Diagnostics; an error blocks publication of the joint candidate set.</returns>
    ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(
        ValidationRequest request,
        CancellationToken cancellationToken
    );
}
