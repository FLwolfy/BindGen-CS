using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Plugins;
using BGCS.Core.Targets;

namespace BGCS.Extension.Android;

/// <summary>Explicitly registers the optional Android parser-target profile without compiling consumer code.</summary>
public sealed class AndroidPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc/>
    public PluginDescriptor descriptor => new("bgcs.extension.android", "4.0.0", [new("bgcs.target.android", ExtensionPort.NativeTargetProvider)]);
    /// <inheritdoc/>
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (System.Text.Json.JsonProperty property in configuration.settings.EnumerateObject())
            throw new ArgumentException("Unknown Android plugin setting: " + property.Name, nameof(configuration));
        registry.RegisterFactory<INativeTargetProvider>("bgcs.target.android", _ => new AndroidTargetProvider());
    }
    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("bgcs.target.android:4")))), []);
    }
}
