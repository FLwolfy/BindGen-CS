using System;
using BGCS.Core.Logging;
using BGCS.Cpp2C.Configuration;

namespace BGCS.Cpp2C.Facade;

/// <summary>
/// Owns the configured C++ generation boundary and the shared operation diagnostics.
/// </summary>
public abstract class BaseGenerator : LoggerBase
{
    /// <summary>
    /// Creates a configured owner without subscribing a console or another global presentation.
    /// </summary>
    /// <param name="config">
    /// Caller-owned configuration; it must remain stable while generation is executing.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Configuration is null.
    /// </exception>
    protected BaseGenerator(Cpp2CGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        this.config = config;
        logLevel = config.logLevel;
    }

    /// <summary>
    /// Gets the configuration used by this generation instance and its extensions.
    /// </summary>
    protected Cpp2CGeneratorConfig config { get; }
}
