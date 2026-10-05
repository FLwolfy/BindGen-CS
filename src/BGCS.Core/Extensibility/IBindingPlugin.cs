namespace BGCS.Core.Extensibility;

/// <summary>
/// Contributes generation services from an explicitly loaded third-party assembly.
/// </summary>
public interface IBindingPlugin
{
    /// <summary>
    /// Gets the nonempty identity used to reject duplicate plugin activation.
    /// </summary>
    string id { get; }

    /// <summary>
    /// Gets the implementation revision included in incremental generation fingerprints.
    /// </summary>
    string version { get; }

    /// <summary>
    /// Gets the service contract revision required by this implementation.
    /// </summary>
    int contractVersion { get; }

    /// <summary>
    /// Registers services into a private candidate that is published only after validation succeeds.
    /// </summary>
    /// <param name="host">
    /// The candidate registry; it must not be retained or modified after this call returns.
    /// </param>
    void Configure(IBindingPluginHost host);
}
