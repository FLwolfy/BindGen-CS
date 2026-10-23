using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Plugins;
using BGCS.Core.Targets;

namespace BGCS.Extension.FreeBsd;

/// <summary>Explicitly registers the optional FreeBsd parser-target profile without compiling consumer code.</summary>
public sealed class FreeBsdPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc/>
    public PluginDescriptor descriptor => new("bgcs.extension.freebsd", "4.1.0", [new("bgcs.target.freebsd", ExtensionPort.NativeTargetProvider)]);
    /// <inheritdoc/>
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (System.Text.Json.JsonProperty property in configuration.settings.EnumerateObject())
            throw new ArgumentException("Unknown FreeBsd plugin setting: " + property.Name, nameof(configuration));
        registry.RegisterFactory<INativeTargetProvider>("bgcs.target.freebsd", _ => new FreeBsdTargetProvider());
    }
    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("bgcs.target.freebsd:4")))), []);
    }
}
