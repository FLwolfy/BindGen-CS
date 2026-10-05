using System;
using BGCS.Configuration;
using BGCS.Core.Logging;

namespace BGCS.Facade;

/// <summary>
/// Owns a C# binding configuration and operation diagnostics for a generation implementation.
/// </summary>
public abstract class BaseGenerator : LoggerBase
{
    /// <summary>
    /// Creates a configuration owner without retaining an implicit console subscription.
    /// </summary>
    /// <param name="config">
    /// Caller-owned configuration that remains stable during each generation attempt.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The configuration is null.
    /// </exception>
    protected BaseGenerator(CsCodeGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        this.config = config;
        logLevel = config.logLevel;
    }

    /// <summary>
    /// Gets the configuration used by this generator; changes apply to subsequent attempts.
    /// </summary>
    public CsCodeGeneratorConfig settings => config;

    /// <summary>
    /// Gets the configured generation policy for derived implementations.
    /// </summary>
    protected CsCodeGeneratorConfig config { get; }
}
