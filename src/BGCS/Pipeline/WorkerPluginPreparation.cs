using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Composition;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Plugins;
using BGCS.Infrastructure.Workers;

namespace BGCS;

internal static class WorkerPluginPreparation
{
    internal static async ValueTask<FrontendRequest> PrepareAsync(
        FrontendRequest request,
        IFrontend frontend,
        ImmutableArray<PluginRegistration> plugins,
        GenerationInputTracker inputs,
        CancellationToken cancellationToken
    ) {
        if (plugins.IsDefaultOrEmpty) return request;
        if (frontend is not FrontendWorkerClient worker)
            throw new NotSupportedException("Worker-only Clang contributors require the isolated built-in Clang frontend.");
        ImmutableArray<WorkerPluginActivation> activations = plugins.Select(registration =>
        {
            LockedPluginManifest locked = registration.Manifest ?? throw new InvalidOperationException("A worker extension omitted its locked manifest.");
            new PluginManifestReader().Verify(locked);
            if (!locked.manifest.workerOnly || locked.manifest.workerParserIdentity != WorkerProtocol.C_PARSER_IDENTITY
                || registration.Descriptor.services.IsDefaultOrEmpty
                || registration.Descriptor.services.Any(service => service.port != ExtensionPort.FrontendFactContributor))
                throw new InvalidOperationException("A worker extension changed its parser identity or exact declared contributor services.");
            PluginConfiguration settings = registration.Configuration ?? throw new InvalidOperationException("A worker extension omitted its frozen settings.");
            return new WorkerPluginActivation(locked.path, locked.manifest, locked.inputs, locked.missingInputs,
                settings.settings, settings.defaultPathBase, settings.valuePathBases, WorkerProtocol.C_PARSER_IDENTITY);
        }).ToImmutableArray();
        FrontendRequest prepared = request with { workerPlugins = activations, workerPluginContext = inputs.WorkerFingerprintContext(request.target), workerPluginCapture = null };
        return await PrepareActivationsAsync(prepared, worker, inputs, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<FrontendRequest> PrepareActivationsAsync(
        FrontendRequest request,
        IFrontend frontend,
        GenerationInputTracker? inputs,
        CancellationToken cancellationToken
    ) {
        if (request.workerPlugins.IsDefault) throw new InvalidDataException("Worker activations must be initialized.");
        if (request.workerPlugins.IsEmpty)
        {
            if (request.workerPluginContext is not null || request.workerPluginCapture is not null)
                throw new InvalidDataException("Worker input state requires declared activations.");
            return request;
        }
        if (frontend is not FrontendWorkerClient worker)
            throw new NotSupportedException("Worker-only Clang contributors require the isolated built-in Clang frontend.");
        var context = request.workerPluginContext ?? throw new InvalidDataException("Worker activations require a frozen fingerprint context.");
        if (context.target.targetId != request.target.targetId || context.target.triple != request.target.triple
            || context.target.platformId != request.target.platformId || context.target.architectureId != request.target.architectureId || context.target.abiId != request.target.abiId
            || !Path.GetFullPath(context.pathBase).Equals(Path.GetFullPath(request.pathBase), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Worker fingerprint provenance differs from the effective request target or source base.");
        foreach (WorkerPluginActivation activation in request.workerPlugins)
        {
            if (activation is null || activation.inputs.IsDefault || activation.missingInputs.IsDefault || activation.valuePathBases is null)
                throw new InvalidDataException("A worker activation omitted its initialized file and settings origins.");
            LockedPluginManifest actual = new PluginManifestReader().Read(activation.manifestPath);
            if (!actual.manifest.workerOnly || actual.manifest.workerParserIdentity != WorkerProtocol.C_PARSER_IDENTITY || activation.parserIdentity != WorkerProtocol.C_PARSER_IDENTITY
                || !Equivalent(actual.manifest, activation.manifest) || !actual.inputs.SequenceEqual(activation.inputs) || !actual.missingInputs.SequenceEqual(activation.missingInputs))
                throw new InvalidDataException("A worker activation does not match its complete locked local manifest and parser identity.");
            var descriptor = PluginDescriptorValidator.Freeze(actual.manifest.plugin);
            if (descriptor.services.IsDefaultOrEmpty || descriptor.services.Any(service => service.port != ExtensionPort.FrontendFactContributor))
                throw new InvalidDataException("A worker-only manifest must declare only parser-specific fact contributors.");
            _ = new PluginConfiguration(descriptor.id, activation.settings, activation.pathBase, activation.valuePathBases);
            new BGCS.Infrastructure.Configuration.ExtensionSchemaValidator().Validate(descriptor.configurationSchema, activation.settings, descriptor.id);
        }
        FrontendRequest prepared = request with { workerPluginCapture = null };
        ImmutableArray<WorkerPluginActivation> activations = prepared.workerPlugins;
        WorkerPluginCapture captured = await worker.CapturePluginInputsAsync(prepared, cancellationToken).ConfigureAwait(false);
        string[] expected = activations.SelectMany(activation => activation.manifest.plugin.services.Select(service => "service:" + service.id)
            .Prepend("plugin:" + activation.manifest.plugin.id)).Order(StringComparer.Ordinal).ToArray();
        if (!captured.fingerprints.Select(fingerprint => fingerprint.id).Order(StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidDataException("The worker did not capture exactly the locked plugin and contributor identities.");
        if (SourceSnapshotVerifier.GetMismatch(captured.inputs, cancellationToken) is string mismatch)
            throw new IOException("Worker extension inputs changed before cache lookup. " + mismatch);
        inputs?.CaptureWorkerPlugins(captured, request.target);
        inputs?.CaptureWorkerStateCheck(async token =>
        {
            WorkerPluginCapture current = await worker.CapturePluginInputsAsync(prepared, token).ConfigureAwait(false);
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(captured, WorkerProtocol.jsonOptions), JsonSerializer.SerializeToElement(current, WorkerProtocol.jsonOptions)))
                throw new IOException("Worker extension state or declared inputs changed before candidate publication.");
        });
        return prepared with { workerPluginCapture = captured };
    }

    private static bool Equivalent<T>(
        T left,
        T right
    )
        => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left, WorkerProtocol.jsonOptions), JsonSerializer.SerializeToElement(right, WorkerProtocol.jsonOptions));
}
