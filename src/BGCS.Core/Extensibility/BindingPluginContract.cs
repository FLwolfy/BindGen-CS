namespace BGCS.Core.Extensibility;

/// <summary>
/// Defines the exact service contract accepted by explicit plugin loading.
/// </summary>
public static class BindingPluginContract
{
    /// <summary>
    /// The contract revision required of plugins loaded by this implementation.
    /// </summary>
    public const int C_CURRENT_VERSION = 1;
}
