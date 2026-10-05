using System;

namespace BGCS.Core.Extensibility;

/// <summary>
/// Accepts typed generation services without exposing generator implementations.
/// </summary>
public interface IBindingPluginHost
{
    /// <summary>
    /// Gets the service contract revision provided by this host.
    /// </summary>
    int contractVersion { get; }

    /// <summary>
    /// Registers a service ordered by descending priority and then ordinal identity.
    /// </summary>
    /// <typeparam name="TService">
    /// The contract under which the service is discoverable.
    /// </typeparam>
    /// <param name="id">
    /// A nonempty identity unique within this service contract.
    /// </param>
    /// <param name="service">
    /// The implementation owned by the configuring plugin.
    /// </param>
    /// <param name="priority">
    /// The ordering priority; larger values precede smaller values.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The identity is empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// The implementation is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The identity is already registered for this contract.
    /// </exception>
    void Register<TService>(
        string id,
        TService service,
        int priority = 0
    )
        where TService : class;
}
