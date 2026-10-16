using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.IO;

namespace BGCS.Composition;

internal sealed class GenerationInputTracker(
    JsonElement configuration,
    string pathBase
)
{
    private readonly JsonElement m_configuration = configuration.Clone();
    private readonly List<SourceSnapshot> m_snapshots = [];
    private readonly List<SourceSnapshot> m_nativeSourceSnapshots = [];
    private readonly List<object> m_nativeSourceEvidence = [];
    private readonly List<object> m_nativeSourceKeys = [];
    private readonly HashSet<object> m_declared = new(ReferenceEqualityComparer.Instance);
    private readonly List<ExtensionFingerprint> m_fingerprints = [];
    private readonly HashSet<string> m_unfingerprinted = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_cacheIneligibilityReasons = new(StringComparer.Ordinal);
    private readonly List<Func<CancellationToken, ValueTask>> m_observations = [];

    internal IReadOnlyList<SourceSnapshot> snapshots => m_snapshots;
    internal IReadOnlyList<SourceSnapshot> nativeSourceSnapshots => m_nativeSourceSnapshots;
    internal IReadOnlyList<object> nativeSourceEvidence => m_nativeSourceEvidence;
    internal IReadOnlyList<object> nativeSourceKeys => m_nativeSourceKeys;
    internal IReadOnlyList<ExtensionFingerprint> fingerprints => m_fingerprints;
    internal IReadOnlyCollection<string> unfingerprintedServices => m_unfingerprinted;
    internal IReadOnlyCollection<string> cacheIneligibilityReasons => m_cacheIneligibilityReasons;

    internal void CaptureArgumentInputs(SourceSnapshot snapshot) => m_snapshots.Add(snapshot);

    internal CacheFingerprintContext WorkerFingerprintContext(NativeTargetDescriptor target) => new(m_configuration, Path.GetFullPath(pathBase), target);

    internal void CaptureWorkerPlugins(
        WorkerPluginCapture capture,
        NativeTargetDescriptor target
    ) {
        if (capture.fingerprints.IsDefault || capture.unfingerprintedServices.IsDefault || capture.inputs is null
            || capture.inputs.files.IsDefault || capture.inputs.searchDirectories.IsDefault || capture.inputs.absentInputs.IsDefault)
            throw new InvalidDataException("The worker returned incomplete extension fingerprint inputs.");
        m_snapshots.Add(capture.inputs);
        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (WorkerPluginFingerprint fingerprint in capture.fingerprints)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint.id);
            ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint.implementation);
            if (!identities.Add(fingerprint.id)) throw new InvalidDataException("The worker repeated an extension fingerprint identity.");
            if (fingerprint.state is { } state)
            {
                _ = new ContentDigest(state.value);
                m_fingerprints.Add(new("worker:" + fingerprint.id, fingerprint.implementation, target.targetId.value, state));
            }
            else m_unfingerprinted.Add("worker:" + fingerprint.id);
        }
        foreach (string service in capture.unfingerprintedServices)
        {
            if (!identities.Contains(service)) throw new InvalidDataException("The worker reported an unknown unfingerprinted extension.");
            m_unfingerprinted.Add("worker:" + service);
            m_cacheIneligibilityReasons.Add("Worker extension has no complete cache fingerprint: " + service);
        }
        foreach (WorkerPluginFingerprint fingerprint in capture.fingerprints.Where(item => item.state is null))
            if (!capture.unfingerprintedServices.Contains(fingerprint.id))
                throw new InvalidDataException("The worker failed to disable an extension without a complete fingerprint.");
    }

    internal void CaptureObservation(Func<CancellationToken, ValueTask> verify) => m_observations.Add(verify);

    internal void CaptureNativeSourceObservation(
        string targetId,
        ContentDigest observation,
        SourceSnapshot snapshot,
        string ownedScratchDirectory,
        IReadOnlyDictionary<string, string> sourcePathMappings
    ) {
        m_nativeSourceSnapshots.Add(snapshot);
        m_nativeSourceKeys.Add(NativeSourceInputIdentity.Create(snapshot, sourcePathMappings));
        // Verification retains the real exclusion. Persisted evidence names only
        // its private role, so the destroyed per-operation directory does not
        // manufacture a source change in otherwise identical generation receipts.
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string scratch = Path.GetFullPath(ownedScratchDirectory);
        if (!snapshot.excludedDirectories.Any(directory => Path.GetFullPath(directory).Equals(scratch, comparison)))
            throw new InvalidDataException("The native source observation did not retain its owned scratch exclusion.");
        m_nativeSourceEvidence.Add(NativeSourceInputIdentity.Evidence(snapshot, sourcePathMappings,
            snapshot.excludedDirectories.Select(directory => Path.GetFullPath(directory).Equals(scratch, comparison)
                ? "native-source-view:/scratch" : directory)));
        string location = typeof(BGCS.NativeBuild.Planning.NativeSourceViewPreprocessor).Assembly.Location;
        if (string.IsNullOrEmpty(location))
        {
            m_cacheIneligibilityReasons.Add("The native source-view implementation has no hashable assembly file.");
            return;
        }
        using FileStream implementation = File.OpenRead(location);
        m_fingerprints.Add(new("BGCS.NativeSourceView", Convert.ToHexStringLower(SHA256.HashData(implementation)), targetId, observation));
    }

    internal void DisableCaching(IEnumerable<string> reasons)
    {
        foreach (string reason in reasons)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            m_cacheIneligibilityReasons.Add(reason);
        }
    }

    internal void Capture(
        object service,
        NativeTargetDescriptor target,
        CancellationToken cancellationToken
    ) {
        CaptureDeclared(service, cancellationToken);
        Type type = service.GetType();
        if (service is not ICacheFingerprintProvider provider)
        {
            if (type != typeof(BGCS.Infrastructure.Workers.FrontendWorkerClient)
                && type != typeof(BGCS.CSharp.Projection.DefaultManagedProjection) && type != typeof(BGCS.CSharp.CSharpBackend))
                m_unfingerprinted.Add(type.FullName ?? type.Name);
            return;
        }
        CacheFingerprintContribution contribution = provider.CaptureFingerprint(new(m_configuration, pathBase, target), cancellationToken)
            ?? throw new InvalidOperationException("An extension fingerprint provider returned no contribution.");
        _ = new ContentDigest(contribution.fingerprint.value);
        string assemblyPath = type.Assembly.Location;
        string implementation = string.IsNullOrEmpty(assemblyPath) ? type.Assembly.ManifestModule.ModuleVersionId.ToString()
            : Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
        m_fingerprints.Add(new(type.FullName ?? type.Name, implementation, target.targetId.value, contribution.fingerprint));
        m_snapshots.Add(new ExternalInputSnapshotBuilder().Capture(contribution.externalInputs, pathBase, cancellationToken));
    }

    internal void CaptureDeclared(
        object service,
        CancellationToken cancellationToken
    ) {
        if (service is IExternalInputProvider provider && m_declared.Add(service))
            m_snapshots.Add(new ExternalInputSnapshotBuilder().Capture(provider.GetExternalInputs(new(m_configuration, pathBase), cancellationToken), pathBase, cancellationToken));
    }

    internal async ValueTask VerifyAsync(CancellationToken cancellationToken)
    {
        await VerifyBeforeAndReplayAsync(cancellationToken).ConfigureAwait(false);
        if (SourceSnapshotBatch.GetMismatch(VerificationSnapshots(), cancellationToken) is string afterMismatch)
            throw new IOException("A declared input changed while compiler observations were revalidated. " + afterMismatch);
    }

    internal async ValueTask VerifyBeforeAndReplayAsync(CancellationToken cancellationToken)
    {
        if (SourceSnapshotBatch.GetMismatch(VerificationSnapshots(), cancellationToken) is string beforeMismatch)
            throw new IOException("An extension's declared external inputs changed during generation. " + beforeMismatch);
        foreach (Func<CancellationToken, ValueTask> verify in m_observations)
            await verify(cancellationToken).ConfigureAwait(false);
    }

    // Enumerate at the verification fence, after any observation callbacks have
    // completed. Never retain successful checks across those callbacks.
    internal IEnumerable<SourceSnapshot> VerificationSnapshots()
    {
        foreach (SourceSnapshot item in m_snapshots) yield return item;
        foreach (SourceSnapshot item in m_nativeSourceSnapshots) yield return item;
    }

    internal sealed record ExtensionFingerprint(
        string serviceType,
        string implementation,
        string targetId,
        ContentDigest state
    );
}
