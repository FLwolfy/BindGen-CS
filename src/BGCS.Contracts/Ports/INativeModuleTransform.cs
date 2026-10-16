using System.Threading;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Transforms frozen native facts without accessing parser objects or mutable configuration.</summary>
public interface INativeModuleTransform
{
    /// <summary>Gets a globally unique extension identity.</summary>
    string id { get; }
    /// <summary>Gets the execution priority; higher priorities run first.</summary>
    int priority { get; }
    /// <summary>Produces a new native module or returns the unchanged immutable input.</summary>
    /// <param name="module">The frozen native facts.</param>
    /// <param name="context">Immutable target, language and source dependencies.</param>
    /// <param name="cancellationToken">Cancels cooperative analysis work.</param>
    /// <returns>The replacement native module.</returns>
    NativeModule Transform(
        NativeModule module,
        AnalysisContext context,
        CancellationToken cancellationToken
    );
}
