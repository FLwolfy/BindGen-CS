using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;

namespace BGCS.Tests.NeutralPlugin;

/// <summary>Customizes built-in managed enum names using Contracts alone.</summary>
public sealed class ManagedNamesPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc/>
    public PluginDescriptor descriptor => new("fixture.managed-names-plugin", "1.0.0",
        [new("fixture.managed-names", ExtensionPort.ManagedApiTransform, 5)])
    {
        configurationSchema = JsonSerializer.SerializeToElement(new
        { type = "object", properties = new { prefix = new { type = "string" } }, additionalProperties = false })
    };
    /// <summary>Gets the service created for the current independent session.</summary>
    public ManagedNamesTransform? transform { get; private set; }
    /// <summary>Gets whether this plugin has been released after its service.</summary>
    public bool disposed { get; private set; }
    /// <inheritdoc/>
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        string prefix = configuration.settings.TryGetProperty("prefix", out JsonElement value) ? value.GetString()! : "custom_";
        registry.RegisterFactory<IManagedApiTransform>("fixture.managed-names", _ => transform = new(prefix));
    }
    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("managed-names-plugin-v1")))), []);
    /// <inheritdoc/>
    public void Dispose()
    {
        if (transform is { disposed: false }) throw new InvalidOperationException("A managed transform must be released before its plugin.");
        disposed = true;
    }
}
