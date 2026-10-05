using System;

namespace BGCS.Configuration;

/// <summary>
/// Validates generator configuration before output is created or replaced.
/// </summary>
public static class ConfigValidator
{
    /// <summary>
    /// Validates declaration mapping, naming, import and output settings before generation begins.
    /// </summary>
    /// <param name="config">Configuration to validate; the instance is not changed.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="InvalidOperationException">One or more configuration settings are invalid.</exception>
    public static void Validate(CsCodeGeneratorConfig config)
    {
        CsCodeGeneratorConfigValidator.Validate(config);
    }
}
