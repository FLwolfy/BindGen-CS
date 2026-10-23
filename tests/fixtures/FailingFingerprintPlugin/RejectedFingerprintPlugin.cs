using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;

namespace BGCS.Tests.Fixtures.FailingFingerprintPlugin;

/// <summary>
/// Models an external plugin whose output state cannot be captured after service configuration.
/// </summary>
public sealed class RejectedFingerprintPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc/>
    public PluginDescriptor descriptor => new("bgcs.fixtures.rejected-fingerprint", "1.0.0", [new("rejected-service", ExtensionPort.NativeModuleTransform)]);

    /// <inheritdoc/>
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    )
        => registry.RegisterFactory<INativeModuleTransform>("rejected-service", _ => new FixtureTransform("rejected-service"));

    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => throw new InvalidOperationException("The configured plugin state cannot produce a fingerprint.");
}

/// <summary>Provides a real Contracts-only external entry for independent loader sessions.</summary>
public sealed class AcceptancePlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc/>
    public PluginDescriptor descriptor => new("bgcs.fixtures.acceptance", "1.0.0", [new("acceptance-probe", ExtensionPort.NativeModuleTransform)]);
    /// <inheritdoc/>
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    )
        => registry.RegisterFactory<INativeModuleTransform>("acceptance-probe", _ => new FixtureTransform("acceptance-probe"));
    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("acceptance-v1")))), []);
}

/// <summary>Models construction failure before any service can be published.</summary>
public sealed class ThrowingConstructorPlugin : IBindingPlugin
{
    /// <summary>Always rejects activation.</summary>
    public ThrowingConstructorPlugin() => throw new InvalidOperationException("The plugin constructor failed.");
    /// <inheritdoc/>
    public PluginDescriptor descriptor => new("bgcs.fixtures.constructor", "1.0.0", [new("constructor-probe", ExtensionPort.NativeModuleTransform)]);
    /// <inheritdoc/>
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) => throw new InvalidOperationException("Unreachable configuration.");
}

internal sealed class FixtureTransform(string identity) : INativeModuleTransform, ICacheFingerprintProvider
{
    public string id => identity;
    public int priority => 0;
    public NativeModule Transform(
        NativeModule module,
        AnalysisContext context,
        CancellationToken cancellationToken
    ) => module;
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))), []);
}
