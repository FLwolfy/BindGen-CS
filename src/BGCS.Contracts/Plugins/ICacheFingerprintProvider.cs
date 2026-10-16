using System.Collections.Immutable;
using System.Text.Json;
using System.Threading;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;
using System;

namespace BGCS.Contracts.Plugins;

/// <summary>Supplies a complete deterministic state identity for an extension's affected cache stages.</summary>
public interface ICacheFingerprintProvider
{
    /// <summary>Captures all artifact-affecting extension state and declares each external local input.</summary>
    /// <param name="context">Frozen settings, target and path ownership.</param>
    /// <param name="cancellationToken">Cancels cooperative fingerprint discovery.</param>
    /// <returns>The exact state identity and complete declared input requests.</returns>
    CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    );
}

/// <summary>Describes the immutable context against which an extension declares its cache inputs.</summary>
public sealed record CacheFingerprintContext
{
    /// <summary>Clones serialized settings so their owning JSON document cannot alter or invalidate the fingerprint context.</summary>
    /// <param name="configuration">The complete frozen settings.</param>
    /// <param name="pathBase">The captured absolute input base.</param>
    /// <param name="target">The fully resolved target and toolchain.</param>
    public CacheFingerprintContext(
        JsonElement configuration,
        string pathBase,
        NativeTargetDescriptor target
    ) {
        if (configuration.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Fingerprint settings must be a complete JSON value.", nameof(configuration));
        ArgumentException.ThrowIfNullOrWhiteSpace(pathBase);
        ArgumentNullException.ThrowIfNull(target);
        if (!System.IO.Path.IsPathFullyQualified(pathBase)) throw new ArgumentException("Fingerprint inputs require an absolute captured path base.", nameof(pathBase));
        this.configuration = configuration.Clone();
        this.pathBase = pathBase;
        this.target = target;
    }
    /// <summary>Gets independently owned frozen serialized settings.</summary>
    public JsonElement configuration { get; }
    /// <summary>Gets the absolute local input base.</summary>
    public string pathBase { get; }
    /// <summary>Gets the frozen native target and toolchain.</summary>
    public NativeTargetDescriptor target { get; }
}

/// <summary>Identifies declared extension state without pretending arbitrary plugin behavior can be independently proven.</summary>
/// <param name="fingerprint">The SHA-256 identity of all private artifact-affecting state.</param>
/// <param name="externalInputs">The complete external input collection, including valid absences.</param>
public sealed record CacheFingerprintContribution(
    ContentDigest fingerprint,
    ImmutableArray<DeclaredExternalInput> externalInputs
);
