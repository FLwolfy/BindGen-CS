using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Core;

namespace BGCS.Tool.Commands;

internal static class DiagnosticPresentation
{
    internal sealed record Options(
        BindingDiagnosticSeverity? minimumLevel = BindingDiagnosticSeverity.Trace,
        BindingDiagnosticSeverity? frontendMinimumLevel = BindingDiagnosticSeverity.Trace
    );

    internal static string[] Extract(
        string[] args,
        out Options options
    ) {
        List<string> remaining = [];
        BindingDiagnosticSeverity? general = BindingDiagnosticSeverity.Trace;
        BindingDiagnosticSeverity? frontend = null;
        bool generalSpecified = false;
        bool frontendSpecified = false;
        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (option is not ("--log-level" or "--frontend-log-level"))
            {
                remaining.Add(option);
                continue;
            }
            if (++index >= args.Length) throw new ArgumentException($"{option} requires a diagnostic severity.");
            BindingDiagnosticSeverity? level = ParseLevel(args[index]);
            if (option == "--log-level")
            {
                if (generalSpecified) throw new ArgumentException("--log-level was supplied more than once.");
                generalSpecified = true;
                general = level;
            }
            else
            {
                if (frontendSpecified) throw new ArgumentException("--frontend-log-level was supplied more than once.");
                frontendSpecified = true;
                frontend = level;
            }
        }
        options = new(general, frontendSpecified ? frontend : general);
        return remaining.ToArray();
    }

    internal static void Write(
        IEnumerable<BindingDiagnostic> diagnostics,
        TextWriter writer,
        Options? options = null
    ) {
        options ??= new();
        foreach (BindingDiagnostic diagnostic in diagnostics)
        {
            BindingDiagnosticSeverity? minimum = diagnostic.code?.StartsWith("BGCS-FRONTEND-", StringComparison.OrdinalIgnoreCase) == true
                ? options.frontendMinimumLevel : options.minimumLevel;
            // Reporting preferences cannot conceal a failed operation or change the structured result.
            if (!diagnostic.isFailure && (minimum is null || diagnostic.severity < minimum)) continue;
            string location = diagnostic.location is { } source ? $"{source.file}({source.line},{source.column}): " : string.Empty;
            writer.WriteLine($"{location}{diagnostic.severity.ToString().ToLowerInvariant()}: {diagnostic.code}: {diagnostic.message}");
        }
    }

    private static BindingDiagnosticSeverity? ParseLevel(string value) => value.ToLowerInvariant() switch
    {
        "trace" => BindingDiagnosticSeverity.Trace,
        "debug" => BindingDiagnosticSeverity.Debug,
        "information" or "info" => BindingDiagnosticSeverity.Information,
        "warning" => BindingDiagnosticSeverity.Warning,
        "error" => BindingDiagnosticSeverity.Error,
        "critical" => BindingDiagnosticSeverity.Critical,
        "none" => null,
        _ => throw new ArgumentException($"Unknown diagnostic severity '{value}'; use trace, debug, information, warning, error, critical or none.")
    };
}
