using System;

namespace BGCS.Contracts.Plugins;

/// <summary>Contributes neutral factories from one independently owned generation-session instance.</summary>
public interface IBindingPlugin : IDisposable
{
    /// <summary>Gets the immutable metadata shared by discovery and every session instance.</summary>
    PluginDescriptor descriptor { get; }
    /// <summary>Registers the declared factories before the session registry is frozen.</summary>
    /// <param name="registry">The temporary registration surface, valid only during this call.</param>
    /// <param name="configuration">The plugin's independently validated, immutable settings namespace.</param>
    void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    );
    /// <summary>Releases the session plugin after all of its instantiated services have released.</summary>
    void IDisposable.Dispose() { }
}
