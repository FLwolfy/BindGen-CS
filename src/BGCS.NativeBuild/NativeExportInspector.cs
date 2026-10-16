using BGCS.Contracts.Artifacts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Infrastructure.Execution;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;

namespace BGCS.NativeBuild;

/// <summary>
/// Verifies exact ABI model symbols against the built binary export table.
/// </summary>
public static partial class NativeExportInspector
{
    /// <summary>Verifies exact required model symbols against an actual native export table.</summary>
    /// <param name="targetIdentifier">The concrete native target identity.</param>
    /// <param name="libraryPath">The candidate native binary.</param>
    /// <param name="requiredExports">Exact linker symbols supplied by the final ABI model.</param>
    /// <param name="cancellationToken">Cancels export inspection and its process tree.</param>
    /// <param name="toolPath">An optional explicitly selected inspection tool.</param>
    /// <param name="outputKind">The artifact's native linking and consumption form.</param>
    /// <returns>Required, observed and missing symbol snapshots.</returns>
    public static Task<NativeExportInspectionResult> InspectAsync(
        string targetIdentifier,
        string libraryPath,
        IEnumerable<string> requiredExports,
        CancellationToken cancellationToken = default,
        string? toolPath = null,
        NativeOutputKind outputKind = NativeOutputKind.SharedLibrary
    )
        => InspectAsync(new TargetResolver().Resolve(new(new(targetIdentifier))), libraryPath, requiredExports, cancellationToken, toolPath, outputKind);
    /// <summary>Verifies exact symbols and physical binary facts without interpreting the target provider's opaque logical identity.</summary>
    /// <param name="target">The resolved platform, architecture and ABI facts.</param>
    /// <param name="libraryPath">The candidate native artifact.</param>
    /// <param name="requiredExports">Exact final linker symbols.</param>
    /// <param name="cancellationToken">Cancels inspection and child processes.</param>
    /// <param name="toolPath">An optional authoritative symbol tool.</param>
    /// <param name="outputKind">The native consumption form.</param>
    /// <returns>Observed and missing exact symbols.</returns>
    public static async Task<NativeExportInspectionResult> InspectAsync(
        NativeTargetDescriptor target,
        string libraryPath,
        IEnumerable<string> requiredExports,
        CancellationToken cancellationToken = default,
        string? toolPath = null,
        NativeOutputKind outputKind = NativeOutputKind.SharedLibrary
    ) {
        ArgumentNullException.ThrowIfNull(requiredExports);
        NativeBinaryIdentity.Validate(libraryPath, target, outputKind);
        string[] expected = requiredExports.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        (string tool, string[] arguments, bool trim) = SelectTool(target.platformId, libraryPath, toolPath, outputKind);
        ProcessExecutionResult result = await ProcessExecutor.ExecuteAsync(tool, arguments, Path.GetDirectoryName(Path.GetFullPath(libraryPath))!, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (result.timedOut || result.exitCode != 0) throw new InvalidOperationException($"Native export inspection failed: {result.standardError}");
        string[] actual = ParseExports(result.standardOutput, trim).ToArray();
        return new(tool, expected, actual, expected.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
    internal static IReadOnlyList<string> ParseExports(
        string output,
        bool trimLeadingUnderscore
    ) {
        SortedSet<string> symbols = new(StringComparer.Ordinal);
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match dumpbin = DumpbinExportRegex().Match(line);
            string? symbol = dumpbin.Success ? dumpbin.Groups[1].Value : NmExportRegex().Match(line) is Match nm && nm.Success ? nm.Groups[1].Value : null;
            if (string.IsNullOrWhiteSpace(symbol))
                continue;
            if (trimLeadingUnderscore && symbol.StartsWith('_'))
                symbol = symbol[1..];
            symbols.Add(symbol);
        }

        return symbols.ToArray();
    }

    private static (string Tool, string[] Arguments, bool TrimLeadingUnderscore) SelectTool(
        string platformId,
        string libraryPath,
        string? toolPath,
        NativeOutputKind outputKind = NativeOutputKind.SharedLibrary
    ) {
        if (outputKind != NativeOutputKind.SharedLibrary)
        {
            string? selected = toolPath is null ? NativeBuildToolDiscovery.FindExecutable("llvm-nm", OperatingSystem.IsWindows() ? "C:/Program Files/LLVM/bin/llvm-nm.exe" : null)
                ?? NativeBuildToolDiscovery.FindExecutable("nm") : NativeBuildToolDiscovery.FindExecutable(toolPath);
            if (selected is null) throw new FileNotFoundException("A static/object symbol inspector is unavailable; configure llvm-nm explicitly.", toolPath);
            bool apple = platformId is "macos" or "ios" or "ios-simulator";
            return (selected, apple ? ["-gU", libraryPath] : ["--extern-only", "--defined-only", libraryPath], apple);
        }
        if (platformId == "windows")
        {
            string? selected = toolPath is null ? NativeBuildToolDiscovery.FindDumpBin() : NativeBuildToolDiscovery.FindExecutable(toolPath);
            return (selected ?? throw new FileNotFoundException("The selected native export inspector is unavailable. Install Visual Studio C++ build tools or configure dumpbin explicitly.", toolPath), ["/NOLOGO", "/EXPORTS", libraryPath], false);
        }
        string inspector = NativeBuildToolDiscovery.FindExecutable(toolPath ?? "nm")
            ?? throw new FileNotFoundException("The selected native export inspector is unavailable; configure its executable explicitly.", toolPath);
        if (platformId is "macos" or "ios" or "ios-simulator")
            return (inspector, ["-gU", libraryPath], true);
        return (inspector, ["-D", "--defined-only", libraryPath], false);
    }

    [GeneratedRegex(@"\b[0-9A-Fa-f]+\s+[A-Za-z]\s+([^\s]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NmExportRegex();
    [GeneratedRegex(@"^\s*\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+([^\s]+)(?:\s*=.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DumpbinExportRegex();
}
