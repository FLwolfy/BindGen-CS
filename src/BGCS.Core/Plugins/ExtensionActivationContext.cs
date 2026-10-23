using System;
using BGCS.Core.Hosting;

namespace BGCS.Core.Plugins;

/// <summary>Provides explicit session facts to a declared factory without a service locator or global state.</summary>
/// <param name="hostServices">The selected host facilities, absent only while creating the host provider itself.</param>
/// <param name="baseDirectory">The captured absolute request base.</param>
public sealed record ExtensionActivationContext(
    HostServices? hostServices,
    string baseDirectory
)
{
    /// <summary>Requires host facilities after the bootstrap provider has been activated.</summary>
    /// <returns>The current session's owned host facilities.</returns>
    /// <exception cref="InvalidOperationException">A bootstrap factory requests facilities that do not exist yet.</exception>
    public HostServices RequireHostServices() => hostServices
        ?? throw new InvalidOperationException("Host facilities are unavailable until the explicitly registered host provider has been activated.");
}
