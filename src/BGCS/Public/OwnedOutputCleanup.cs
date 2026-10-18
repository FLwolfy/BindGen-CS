using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Infrastructure.Publishing;

namespace BGCS;

/// <summary>Clears only complete artifact sets whose ownership and protected input boundaries are verified.</summary>
public sealed partial class BindingHost
{
    /// <summary>Clears a verified owned artifact set without parsing headers or partially deleting its ownership manifest.</summary>
    /// <param name="request">The expected library owner, configured output and protected input paths.</param>
    /// <param name="cancellationToken">Cancels waiting for output ownership or before replacement.</param>
    /// <returns>Whether an existing owned output was cleared; its small valid ownership marker is retained for rebuilding.</returns>
    public async Task<bool> ClearOwnedOutputAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_closing, this);
            m_sessions++;
        }
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(request.configuration.execution.sessionTimeoutSeconds));
        try
        {
            var configuration = request.configuration;
            string output = Path.GetFullPath(configuration.output.root, request.baseDirectory);
            string[] protectedInputs = configuration.source.headers.Concat(configuration.source.includeDirectories).Concat(configuration.source.systemIncludeDirectories)

                .Concat(configuration.cpp.nativeShims.SelectMany(shim => shim.publicHeaders.Concat(shim.sourceFiles)))
                .Concat(configuration.targets.SelectMany(target => target.settings.systemIncludeDirectories.Concat(target.settings.cxxSystemIncludeDirectories)
                    .Concat(target.settings.sysRoot is null ? [] : new[] { target.settings.sysRoot })))
                .Concat(configuration.api.projectionInputs.sources).Concat(configuration.api.projectionInputs.references).Append(configuration.cache.directory)
                .Select(path => Path.GetFullPath(path, request.baseDirectory)).Concat(request.configurationInputs.SelectMany(input => new[] { input.path, input.resolvedPath }))
                .Concat(request.pluginInputs.SelectMany(input => new[] { input.path, input.resolvedPath }))
                .Concat(request.configurationPath is null ? [] : new[] { request.configurationPath }).ToArray();
            return await Task.Run(() => ArtifactPublisher.ClearOwned(output, configuration.library.id, protectedInputs, cancellation.Token), cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (m_gate)
                if (--m_sessions == 0 && m_closing) m_drained.TrySetResult();
        }
    }
}
