using System.Threading;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Plans native construction without starting tools or publishing output.</summary>
public interface INativeBuildProvider
{
    /// <summary>Gets the stable native build provider identity.</summary>
    string id { get; }
    /// <summary>Produces a complete immutable compiler and linker plan.</summary>
    /// <param name="request">The frozen native construction inputs.</param>
    /// <param name="cancellationToken">Cancels cooperative planning.</param>
    /// <returns>The ordered shell-free build plan.</returns>
    NativeBuildPlan Plan(
        NativeBuildRequest request,
        CancellationToken cancellationToken
    );
}
