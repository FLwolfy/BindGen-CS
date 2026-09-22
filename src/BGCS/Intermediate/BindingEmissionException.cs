namespace BGCS.Intermediate;

/// <summary>
/// Reports that an emitter cannot preserve one or more declarations from a binding module.
/// </summary>
public sealed class BindingEmissionException : InvalidOperationException
{
    public BindingEmissionException(IReadOnlyList<BindingDiagnostic> diagnostics)
        : base(CreateMessage(diagnostics))
    {
        Diagnostics = diagnostics?.ToArray() ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Gets the structured diagnostics that prevented emission.
    /// </summary>
    public IReadOnlyList<BindingDiagnostic> Diagnostics { get; }

    private static string CreateMessage(IReadOnlyList<BindingDiagnostic>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Count == 0
            ? "Binding emission failed without a diagnostic."
            : string.Join(Environment.NewLine, diagnostics.Select(diagnostic =>
                $"{diagnostic.Code ?? "BGCS"}: {diagnostic.Message}"));
    }
}
