using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using System.Threading;

namespace BGCS.Interop.DotNet;

/// <summary>Explicitly registers .NET execution and direct-call ABI providers.</summary>
public sealed class DotNetInteropPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => ExtensionFingerprint.Capture(GetType(), cancellationToken);

    /// <inheritdoc />
    public PluginDescriptor descriptor => new("bgcs.extensions.dotnet", "4.0.0", [
        new("dotnet.execution", ExtensionPort.ManagedRuntimeProvider),
        new("dotnet.call-abi", ExtensionPort.CallAbiProvider)]);

    /// <inheritdoc />
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        registry.RegisterFactory<IManagedRuntimeProvider>("dotnet.execution", static _ => new DotNetRuntimeProvider());
        registry.RegisterFactory<ICallAbiProvider>("dotnet.call-abi", static _ => new DotNetCallAbiProvider());
    }
}
