using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Plugins;
using BGCS.Core.Requests;

namespace BGCS.Core.Ports;

/// <summary>Exposes detached execution and worker-extension facts for an explicitly selected isolated parser.</summary>
public interface IIsolatedFrontend : IFrontend
{
    /// <summary>Gets the exact implementation identity required by this parser's worker extensions.</summary>
    string parserIdentity { get; }

    /// <summary>Captures the execution bytes and environment identity before admitting reusable detached facts.</summary>
    /// <param name="cancellationToken">Cancels execution-input verification.</param>
    /// <returns>The complete execution identity and its independently checkable inputs.</returns>
    FrontendExecutionIdentity CaptureExecutionIdentity(CancellationToken cancellationToken);

    /// <summary>Captures declared worker-only extension state without retaining a live parser model.</summary>
    /// <param name="request">The exact selected target and locked worker extension activations.</param>
    /// <param name="cancellationToken">Cancels worker startup and bounded input capture.</param>
    /// <returns>The detached state and all declared worker inputs.</returns>
    ValueTask<WorkerPluginCapture> CapturePluginInputsAsync(
        FrontendRequest request,
        CancellationToken cancellationToken
    );
}

/// <summary>Identifies an isolated frontend's complete reusable execution state.</summary>
/// <param name="digest">The canonical implementation and environment digest.</param>
/// <param name="snapshot">The execution files and their independently checked content identities.</param>
public sealed record FrontendExecutionIdentity(
    ContentDigest digest,
    SourceSnapshot snapshot
);
