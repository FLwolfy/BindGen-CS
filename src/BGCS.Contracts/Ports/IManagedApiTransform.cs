using System.Threading;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Customizes an already projected managed model while preserving its identical frozen native ABI.</summary>
public interface IManagedApiTransform
{
    /// <summary>Gets the globally unique service identity.</summary>
    string id { get; }
    /// <summary>Gets descending execution priority; equal priorities use ordinal identity order.</summary>
    int priority { get; }
    /// <summary>Changes managed names, documentation or explicit conversions without replacing the built-in projection.</summary>
    /// <param name="model">The immutable managed projection and unchanged native ABI.</param>
    /// <param name="context">The resolved target and immutable source provenance.</param>
    /// <param name="cancellationToken">Cancels cooperative pure model processing.</param>
    /// <returns>A new managed presentation referencing the identical interoperability module.</returns>
    ManagedApiModel Transform(
        ManagedApiModel model,
        AnalysisContext context,
        CancellationToken cancellationToken
    );
}
