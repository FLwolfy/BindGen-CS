using System;
using System.IO;

namespace BGCS.Core.IO;

/// <summary>
/// Resolves configuration-relative paths consistently before toolchain or generation composition.
/// </summary>
public static class ConfigurationPath
{
    /// <summary>
    /// Expands environment variables and resolves file-system inputs against their document directory.
    /// </summary>
    /// <param name="value">
    /// The configured path or executable name; null or whitespace represents an unspecified input.
    /// </param>
    /// <param name="baseDirectory">
    /// The absolute directory containing the configuration document.
    /// </param>
    /// <param name="allowCommandName">
    /// Whether a bare executable name may remain unresolved for operating-system PATH lookup.
    /// </param>
    /// <returns>
    /// The expanded absolute path, a permitted bare executable name, or null when unspecified.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// A specified path or its base directory is invalid.
    /// </exception>
    public static string? Resolve(
        string? value,
        string baseDirectory,
        bool allowCommandName
    ) {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string expanded = Environment.ExpandEnvironmentVariables(value);
        if (allowCommandName && !Path.IsPathRooted(expanded) && !expanded.Contains('/') && !expanded.Contains('\\'))
            return expanded;
        return Path.GetFullPath(expanded, baseDirectory);
    }
}
