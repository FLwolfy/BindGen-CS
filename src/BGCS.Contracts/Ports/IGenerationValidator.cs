using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Checks final candidate contracts and artifacts without owning formal publication.</summary>
public interface IGenerationValidator
{
    /// <summary>Gets the globally unique gate identity.</summary>
    string id { get; }
    /// <summary>Gets descending validation order, followed by stable ordinal identity.</summary>
    int priority { get; }
    /// <summary>Validates one target's complete candidate after the requested build stages.</summary>
    /// <param name="request">The immutable target, ABI and verified artifact read surface.</param>
    /// <param name="cancellationToken">Cancels cooperative validation.</param>
    /// <returns>Diagnostics; an error blocks publication of the joint candidate set.</returns>
    ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(
        ValidationRequest request,
        CancellationToken cancellationToken
    );
}
