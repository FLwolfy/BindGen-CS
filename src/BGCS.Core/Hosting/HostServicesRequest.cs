using System.Text.Json;

namespace BGCS.Core.Hosting;

/// <summary>Supplies an explicit bootstrap base and detached host-extension settings.</summary>
/// <param name="baseDirectory">The caller's absolute bootstrap path base.</param>
/// <param name="settings">The host provider's validated immutable settings.</param>
public sealed record HostServicesRequest(
    string baseDirectory,
    JsonElement settings
);
