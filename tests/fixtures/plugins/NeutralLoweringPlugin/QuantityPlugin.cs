using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;

namespace BGCS.Tests.NeutralPlugin;

/// <summary>Contributes a neutral quantity lowering through per-session plugin registration.</summary>
public sealed class QuantityPlugin : IBindingPlugin
{
    /// <inheritdoc/>
    public PluginDescriptor descriptor => new("fixture.quantity-plugin", "1.0.0", [new("fixture.quantity", ExtensionPort.CppTypeLowering, 20), new("fixture.quantity-validation", ExtensionPort.GenerationValidator), new("fixture.quantity-processing", ExtensionPort.ArtifactProcessor)]);
    /// <summary>Gets the lowering created by the current session, when requested.</summary>
    public QuantityLowering? lowering { get; private set; }
    /// <summary>Gets the independent final gate created by the session.</summary>
    public QuantityValidator? validator { get; private set; }
    /// <summary>Gets the independent candidate text processor created by the session.</summary>
    public QuantityArtifactProcessor? processor { get; private set; }
    /// <summary>Gets whether the plugin has released its session state.</summary>
    public bool disposed { get; private set; }
    /// <inheritdoc/>
    public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration)
    {
        registry.RegisterFactory<ICppTypeLowering>("fixture.quantity", () => lowering = new());
        registry.RegisterFactory<IGenerationValidator>("fixture.quantity-validation", () => validator = new());
        registry.RegisterFactory<IArtifactProcessor>("fixture.quantity-processing", () => processor = new());
    }
    /// <inheritdoc/>
    public void Dispose() => disposed = true;
}
