using System.Threading;
using BGCS.Core.Managed;
using BGCS.Core.Requests;

namespace BGCS.Core.Ports;

/// <summary>Customizes an already projected managed model while preserving its identical frozen native ABI.</summary>
public interface IManagedApiTransform
{
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
