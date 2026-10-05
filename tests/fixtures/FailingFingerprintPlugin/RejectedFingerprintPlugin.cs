using System;
using BGCS.Core.Extensibility;

namespace BGCS.Tests.Fixtures.FailingFingerprintPlugin;

/// <summary>
/// Models an external plugin whose output state cannot be captured after service configuration.
/// </summary>
public sealed class RejectedFingerprintPlugin : IBindingPlugin
{
    /// <inheritdoc/>
    public string id => "bgcs.fixtures.rejected-fingerprint";

    /// <inheritdoc/>
    public string version => "fixture";

    /// <inheritdoc/>
    public int contractVersion => BindingPluginContract.C_CURRENT_VERSION;

    /// <inheritdoc/>
    public void Configure(IBindingPluginHost host)
        => host.Register<ICacheFingerprintProvider>("rejected-service", new InvalidFingerprint());

    private sealed class InvalidFingerprint : ICacheFingerprintProvider
    {
        /// <inheritdoc/>
        public string GetCacheFingerprint()
            => throw new InvalidOperationException("The configured plugin state cannot produce a fingerprint.");
    }
}
