using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Execution;

namespace BGCS.NativeBuild.Planning;

// Preprocessing consumes source directives, including #warning. Preserve the
// actual compiler's diagnostics before the detached frontend sees expanded text.
internal static class NativeSourceViewDiagnostics
{
    private static readonly Regex Msvc = new(@"^(?<file>.+)\((?<line>[0-9]+)(?:,(?<column>[0-9]+))?\)\s*:\s*(?<severity>warning|error|fatal error|note)\s*(?<code>[A-Z]+[0-9]+)?\s*:\s*(?<message>.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex Gnu = new(@"^(?<file>.+?):(?<line>[0-9]+):(?:(?<column>[0-9]+):)?\s*(?<severity>warning|error|fatal error|note):\s*(?<message>.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex MsvcDriver = new(@"^cl(?:\.exe)?\s*:\s*.*?\b(?<severity>warning|error|fatal error)\s+(?<code>[A-Z]+[0-9]+)\s*:\s*(?<message>.+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Driver = new(@"^(?:[^:\r\n]+\s*:\s*)?(?:command line\s*(?::\s*)?)?(?<severity>warning|error|fatal error|note)\s*(?<code>[A-Z]+[0-9]+)?\s*:\s*(?<message>.+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static ImmutableArray<BindingDiagnostic> Read(
        ImmutableArray<ProcessExecutionResult> executions,
        FrontendRequest source
    ) {
        HashSet<BindingDiagnostic> diagnostics = [];
        foreach (var execution in executions)
            foreach (string stream in new[] { execution.standardOutput, execution.standardError })
                foreach (string line in stream.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    Match match = Msvc.Match(line);
                    if (!match.Success) match = Gnu.Match(line);
                    if (!match.Success) match = MsvcDriver.Match(line);
                    if (!match.Success) match = Driver.Match(line);
                    if (!match.Success) continue;
                    BindingDiagnosticSeverity severity = match.Groups["severity"].Value.ToLowerInvariant() switch
                    {
                        "warning" => BindingDiagnosticSeverity.Warning,
                        "note" => BindingDiagnosticSeverity.Information,
                        _ => BindingDiagnosticSeverity.Error
                    };
                    string code = match.Groups["code"].Value;
                    string message = (code.Length == 0 ? string.Empty : code + ": ") + match.Groups["message"].Value;
                    SourceLocation? location = null;
                    string file = match.Groups["file"].Value;
                    if (file.Length != 0 && !(file.StartsWith('<') && file.EndsWith('>'))
                        && int.TryParse(match.Groups["line"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0)
                    {
                        string path = Path.GetFullPath(file, source.pathBase);
                        if (source.sourcePathMappings.TryGetValue(path, out string? logical)) path = logical;
                        int column = int.TryParse(match.Groups["column"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int actualColumn) ? actualColumn : 0;
                        location = new(path, number, column);
                    }
                    diagnostics.Add(new(severity, message, "BGCS-FRONTEND-NATIVE") { location = location });
                }
        return diagnostics.OrderBy(diagnostic => diagnostic.location?.file, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.location?.line).ThenBy(diagnostic => diagnostic.location?.column)
            .ThenBy(diagnostic => diagnostic.severity).ThenBy(diagnostic => diagnostic.message, StringComparer.Ordinal).ToImmutableArray();
    }
}
