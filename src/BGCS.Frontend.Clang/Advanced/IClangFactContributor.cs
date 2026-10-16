using System.Threading;
using BGCS.Contracts.Native;

namespace BGCS.Frontend.Clang.Advanced;

/// <summary>Adds detached native facts while the pinned Clang translation unit is alive inside a parser worker.</summary>
public interface IClangFactContributor
{
    /// <summary>Produces the next immutable fact graph without retaining native cursors or changing ordinary lowering contracts.</summary>
    /// <param name="context">Live Clang access valid only for this synchronous invocation.</param>
    /// <param name="module">The detached facts produced by the frontend or preceding contributor.</param>
    /// <param name="cancellationToken">Cancels cooperative fact inspection.</param>
    /// <returns>A complete detached native module for the same target.</returns>
    NativeModule Contribute(
        ClangFactContributionContext context,
        NativeModule module,
        CancellationToken cancellationToken
    );
}
