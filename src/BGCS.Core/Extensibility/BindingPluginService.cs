namespace BGCS.Core.Extensibility;

/// <summary>
/// Describes one immutable service registration in a deterministic discovery snapshot.
/// </summary>
/// <typeparam name="TService">
/// The contract implemented by the registered service.
/// </typeparam>
/// <param name="id">
/// The registration identity unique within the service contract.
/// </param>
/// <param name="priority">
/// The descending discovery priority.
/// </param>
/// <param name="service">
/// The plugin-owned implementation; the snapshot does not transfer ownership.
/// </param>
public sealed record BindingPluginService<TService>(
    string id,
    int priority,
    TService service
)
    where TService : class;
