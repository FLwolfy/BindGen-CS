using System.Threading;
using BGCS.Core.Interop;
using BGCS.Core.Requests;

namespace BGCS.Core.Ports;

/// <summary>Transforms final native interoperability semantics using detached, immutable model inputs.</summary>
public interface IInteropTransform
{
    /// <summary>Produces an immutable interoperability module without retaining native parser state.</summary>
    /// <param name="module">The final native ABI and semantics.</param>
    /// <param name="context">Resolved target and immutable input provenance.</param>
    /// <param name="cancellationToken">Cancels pure transformation work.</param>
    /// <returns>The transformed final native contract.</returns>
    InteropModule Transform(
        InteropModule module,
        AnalysisContext context,
        CancellationToken cancellationToken
    );
}
