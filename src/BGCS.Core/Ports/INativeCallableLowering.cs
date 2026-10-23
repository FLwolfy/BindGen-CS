using System.Threading;
using BGCS.Core.Bridge;
using BGCS.Core.Native;
using BGCS.Core.Requests;

namespace BGCS.Core.Ports;

/// <summary>Extends native callable planning without depending on a frontend AST.</summary>
public interface INativeCallableLowering
{
    /// <summary>Tests whether this extension exclusively claims the callable.</summary>
    /// <param name="callable">The immutable native declaration.</param>
    /// <param name="context">Target and lowering inputs.</param>
    /// <returns>Whether this extension supports the callable.</returns>
    bool CanLower(
        NativeDeclaration callable,
        LoweringContext context
    );
    /// <summary>Produces a typed export action using registered C shapes.</summary>
    /// <param name="callable">The immutable native declaration.</param>
    /// <param name="context">Target and lowering inputs.</param>
    /// <param name="cancellationToken">Cancels cooperative planning.</param>
    /// <returns>The detached bridge export.</returns>
    NativeCallableLoweringPlan Lower(
        NativeDeclaration callable,
        LoweringContext context,
        CancellationToken cancellationToken
    );
}
