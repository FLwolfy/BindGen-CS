namespace BGCS.Cpp2C.Configuration;

/// <summary>
/// Provides independently mutable default configuration values for a new generation owner.
/// </summary>
public partial class Cpp2CGeneratorConfig
{
    /// <summary>
    /// Gets a fresh mutable default configuration; modifying it does not affect later callers.
    /// </summary>
    public static Cpp2CGeneratorConfig @default => new();
}
