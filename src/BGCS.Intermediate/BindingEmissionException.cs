using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Intermediate;

/// <summary>
/// Reports that an emitter cannot preserve one or more declarations from a binding module.
/// </summary>
public sealed class BindingEmissionException : InvalidOperationException
{
    /// <summary>
    /// Captures the diagnostics that prevented a faithful output representation.
    /// </summary>
    /// <param name="diagnostics">Diagnostics copied into a read-only snapshot owned by this exception.</param>
    /// <exception cref="ArgumentNullException">The diagnostic list is null.</exception>
    public BindingEmissionException(IReadOnlyList<BindingDiagnostic> diagnostics) : base(CreateMessage(diagnostics))
    {
        this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    /// <summary>
    /// Gets the structured diagnostics that prevented emission.
    /// </summary>
    public IReadOnlyList<BindingDiagnostic> diagnostics { get; }

    private static string CreateMessage(IReadOnlyList<BindingDiagnostic>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Count == 0 ? "Binding emission failed without a diagnostic." : string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.code ?? "BGCS"}: {diagnostic.message}"));
    }
}
