using System;
using BGCS.Intermediate;

namespace BGCS.Facade;

/// <summary>
/// Generates a complete binding module from a configuration file through the shared generation pipeline.
/// </summary>
public static class BindingGenerator
{
    /// <summary>
    /// Loads and validates configuration, analyzes the native declarations and installs completed output.
    /// </summary>
    /// <param name="configPath">
    /// The configuration document; relative input paths are resolved against its directory.
    /// </param>
    /// <param name="outputPath">
    /// An optional output override relative to the configuration directory, or null to use configured output.
    /// </param>
    /// <returns>
    /// The analyzed module, diagnostics and committed files. Parser or generation errors return an unsuccessful result.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// A configuration value is invalid.
    /// </exception>
    /// <exception cref="System.IO.IOException">
    /// Configuration or output cannot be read or committed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Generation violates its completion contract or the configured parser or extension cannot execute.
    /// </exception>
    public static BindingGenerationResult<BindingModule> Generate(
        string configPath,
        string? outputPath = null
    ) {
        CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
        generator.GenerateConfigured(outputPath);
        return generator.lastResult
            ?? throw new InvalidOperationException("The generation pipeline completed without publishing a result.");
    }
}
