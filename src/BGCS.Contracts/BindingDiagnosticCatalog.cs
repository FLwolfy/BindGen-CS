using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Contracts;

/// <summary>
/// Provides programmatic explanations for every diagnostic identifier emitted by the generators.
/// </summary>
public static partial class BindingDiagnosticCatalog
{
    private static readonly BindingDiagnosticDescriptor[] Descriptors = CreateDescriptors();

    private static readonly IReadOnlyDictionary<string, BindingDiagnosticDescriptor> ByCode =
        Descriptors.ToDictionary(static descriptor => descriptor.code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets immutable diagnostic descriptors ordered by code.
    /// </summary>
    public static IReadOnlyList<BindingDiagnosticDescriptor> all { get; } =
        Array.AsReadOnly(Descriptors.OrderBy(static descriptor => descriptor.code, StringComparer.Ordinal).ToArray());

    /// <summary>
    /// Resolves a diagnostic explanation using a trimmed, case-insensitive identifier.
    /// </summary>
    /// <param name="code">Diagnostic identifier; null or whitespace is treated as absent.</param>
    /// <param name="descriptor">The matching explanation, or null when no identifier matches.</param>
    /// <returns>True when a descriptor was found; otherwise false.</returns>
    public static bool TryGet(
        string? code,
        out BindingDiagnosticDescriptor descriptor
    ) {
        if (string.IsNullOrWhiteSpace(code))
        {
            descriptor = null!;
            return false;
        }

        return ByCode.TryGetValue(code.Trim(), out descriptor!);
    }
}
