using System.Collections.Generic;
using BGCS.Intermediate.Emission;

namespace BGCS.Intermediate.Bridges;

/// <summary>
/// Writes a frozen bridge module without accessing a parser or running lowering services.
/// </summary>
public interface ICppBridgeEmitter
{
    /// <summary>
    /// Writes the native artifacts beneath the caller-owned staging root.
    /// </summary>
    /// <param name = "module">
    /// The frozen target-specific bridge facts and source operations.
    /// </param>
    /// <param name = "context">
    /// The independent output request; bridge artifact paths define the native layout.
    /// </param>
    /// <returns>
    /// The emitted file paths, or an empty list for an empty module.
    /// </returns>
    IReadOnlyList<string> Emit(
        CppBridgeModule module,
        EmissionContext context
    );
}
