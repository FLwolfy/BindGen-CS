namespace BGCS.Configuration;

/// <summary>
/// Supplies the unmanaged boolean carrier needed when constructing a callable signature.
/// </summary>
public interface IGeneratorConfig
{
    /// <summary>
    /// Resolves the managed spelling of the configured native boolean carrier.
    /// </summary>
    /// <returns>
    /// A directly usable unmanaged carrier name, such as Bool8, Bool32, byte, or int.
    /// </returns>
    string GetBoolType();
}
