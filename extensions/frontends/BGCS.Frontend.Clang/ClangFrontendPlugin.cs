using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Frontend.Clang.Workers;

namespace BGCS.Frontend.Clang;

/// <summary>Explicitly registers an isolated Clang frontend and its application-deployed parser resources.</summary>
public sealed class ClangFrontendPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc/>
    public PluginDescriptor descriptor => new("bgcs.frontend.clang", "4.1.2", [new("bgcs.frontend.clang.worker", ExtensionPort.Frontend)])
    {
        configurationSchema = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","additionalProperties":false,"properties":{
              "workerRoot":{"type":"string","minLength":1},
              "parseTimeoutSeconds":{"type":"integer","minimum":1,"maximum":86400}}}
            """)
    };

    /// <inheritdoc/>
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configuration);
        string? workerRoot = configuration.settings.TryGetProperty("workerRoot", out _) ? configuration.ResolveInputPath("/workerRoot") : null;
        TimeSpan timeout = TimeSpan.FromSeconds(configuration.settings.TryGetProperty("parseTimeoutSeconds", out JsonElement seconds) ? seconds.GetInt32() : 300);
        registry.RegisterFactory<IFrontend>("bgcs.frontend.clang.worker", context =>
        {
            var host = context.RequireHostServices();
            return new FrontendWorkerClient(WorkerAssetLocator.Locate(host, workerRoot), host, timeout);
        });
    }

    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ClangParserIdentity.C_IDENTITY)))), []);
    }
}
