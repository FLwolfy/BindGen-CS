using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Core.Logging;
using BGCS.Intermediate;

namespace BGCS.Tool.Output;

/// <summary>
/// Writes structured generation failures in a stable, actionable CLI format.
/// </summary>
internal static class GenerationDiagnosticWriter
{
    internal static IDisposable Attach(
        LoggerBase source,
        TextWriter output
    ) => new LogSubscription(source, output);

    internal static void WriteFailure<TModule>(
        BindingGenerationResult<TModule>? result,
        TextWriter error
    )
        where TModule : class
    {
        ArgumentNullException.ThrowIfNull(error);
        if (result == null || result.diagnostics.Count == 0)
        {
            error.WriteLine("error: binding generation failed without a structured diagnostic.");
            return;
        }

        BindingDiagnosticSeverity minimumSeverity = result.diagnostics.Any(diagnostic => diagnostic.severity >= BindingDiagnosticSeverity.Error) ? BindingDiagnosticSeverity.Error : BindingDiagnosticSeverity.Warning;
        HashSet<(string? Code, string Message)> written = [];
        HashSet<string> explainableCodes = new(StringComparer.OrdinalIgnoreCase);
        foreach (BindingDiagnostic diagnostic in result.diagnostics.Where(diagnostic => diagnostic.severity >= minimumSeverity))
        {
            if (!written.Add((diagnostic.code, diagnostic.message)))
                continue;
            string code = string.IsNullOrWhiteSpace(diagnostic.code) ? string.Empty : $" {diagnostic.code}";
            error.WriteLine($"{diagnostic.severity.ToString().ToLowerInvariant()}:{code}: {diagnostic.message}");
            if (!string.IsNullOrWhiteSpace(diagnostic.code) && BindingDiagnosticCatalog.TryGet(diagnostic.code, out _))
                explainableCodes.Add(diagnostic.code);
        }

        if (written.Count == 0)
            error.WriteLine("error: binding generation failed without an error-level structured diagnostic.");
        foreach (string code in explainableCodes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            error.WriteLine($"help: run 'bindgen-cs explain {code}' for cause and resolution guidance.");
    }

    private sealed class LogSubscription : IDisposable
    {
        private LoggerBase? m_source;
        private readonly TextWriter m_output;

        internal LogSubscription(
            LoggerBase source,
            TextWriter output
        ) {
            m_source = source;
            m_output = output;
            source.LogEvent += Write;
        }

        public void Dispose()
        {
            if (m_source is not { } source)
                return;
            m_source = null;
            source.LogEvent -= Write;
        }

        private void Write(
            LogSeverity severity,
            string message
        ) => m_output.WriteLine(new LogMessage(severity, message));
    }
}
