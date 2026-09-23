using BGCS.Intermediate;

namespace BGCS.Tool.Commands;

/// <summary>
/// Writes structured generation failures in a stable, actionable CLI format.
/// </summary>
internal static class GenerationDiagnosticWriter
{
    internal static void WriteFailure(BindingGenerationResult? result, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (result == null || result.Diagnostics.Count == 0)
        {
            error.WriteLine("error: binding generation failed without a structured diagnostic.");
            return;
        }

        BindingDiagnosticSeverity minimumSeverity = result.Diagnostics.Any(diagnostic =>
            diagnostic.Severity >= BindingDiagnosticSeverity.Error)
            ? BindingDiagnosticSeverity.Error
            : BindingDiagnosticSeverity.Warning;
        HashSet<(string? Code, string Message)> written = [];
        HashSet<string> explainableCodes = new(StringComparer.OrdinalIgnoreCase);
        foreach (BindingDiagnostic diagnostic in result.Diagnostics
                     .Where(diagnostic => diagnostic.Severity >= minimumSeverity))
        {
            if (!written.Add((diagnostic.Code, diagnostic.Message)))
                continue;
            string code = string.IsNullOrWhiteSpace(diagnostic.Code) ? string.Empty : $" {diagnostic.Code}";
            error.WriteLine($"{diagnostic.Severity.ToString().ToLowerInvariant()}:{code}: {diagnostic.Message}");
            if (!string.IsNullOrWhiteSpace(diagnostic.Code) && BindingDiagnosticCatalog.TryGet(diagnostic.Code, out _))
                explainableCodes.Add(diagnostic.Code);
        }

        if (written.Count == 0)
            error.WriteLine("error: binding generation failed without an error-level structured diagnostic.");

        foreach (string code in explainableCodes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            error.WriteLine($"help: run 'bindgen-cs explain {code}' for cause and resolution guidance.");
    }
}
