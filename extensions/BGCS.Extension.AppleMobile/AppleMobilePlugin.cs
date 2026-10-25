using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Plugins;
using BGCS.Core.Targets;

namespace BGCS.Extension.AppleMobile;

/// <summary>Explicitly registers the optional AppleMobile parser-target profile without compiling consumer code.</summary>
public sealed class AppleMobilePlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc/>
    public PluginDescriptor descriptor => new("bgcs.extension.applemobile", "4.1.2", [new("bgcs.target.applemobile", ExtensionPort.NativeTargetProvider)]);
    /// <inheritdoc/>
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (System.Text.Json.JsonProperty property in configuration.settings.EnumerateObject())
            throw new ArgumentException("Unknown AppleMobile plugin setting: " + property.Name, nameof(configuration));
        registry.RegisterFactory<INativeTargetProvider>("bgcs.target.applemobile", _ => new AppleMobileTargetProvider());
    }
    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("bgcs.target.applemobile:4")))), []);
    }
}
