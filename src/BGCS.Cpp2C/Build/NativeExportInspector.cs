using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Core.Execution;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Reads expected symbols from generated public headers and verifies the built binary export table.
/// </summary>
public static partial class NativeExportInspector
{
    /// <summary>
    /// Compares declarations from generated public headers against the actual binary export table.
    /// </summary>
    /// <param name="manifest">Validated bridge compiler contract.</param>
    /// <param name="manifestPath">Path origin used to resolve generated public headers.</param>
    /// <param name="libraryPath">Built native binary to inspect.</param>
    /// <param name="toolPath">Optional inspection executable; null discovers a target-compatible tool.</param>
    /// <returns>Expected, actual and missing symbol snapshots.</returns>
    /// <exception cref="FileNotFoundException">A public header or required inspection tool is missing.</exception>
    /// <exception cref="InvalidOperationException">The tool fails or times out.</exception>
    public static NativeExportInspectionResult Inspect(
        CppBridgeBuildManifest manifest,
        string manifestPath,
        string libraryPath,
        string? toolPath = null
    ) {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        string root = NativeBuildPaths.GetManifestDirectory(manifestPath);
        string[] expected = ReadExpectedSymbols(manifest.publicHeaderFiles.Select(path => NativeBuildPaths.Resolve(root, path))).ToArray();
        (string tool, string[] arguments, bool trimLeadingUnderscore) = SelectTool(manifest.targetIdentifier, libraryPath, toolPath);
        string output = Execute(tool, arguments, root);
        string[] actual = ParseExports(output, trimLeadingUnderscore).ToArray();
        string[] missing = expected.Except(actual, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return new(tool, expected, actual, missing);
    }

    /// <summary>
    /// Extracts exported bridge symbols from generated C headers in deterministic order.
    /// </summary>
    /// <param name="headerPaths">Generated public headers to inspect.</param>
    /// <returns>Distinct expected export names sorted with ordinal comparison.</returns>
    /// <exception cref="ArgumentNullException">The header sequence is null.</exception>
    /// <exception cref="FileNotFoundException">A selected public header does not exist.</exception>
    public static IReadOnlyList<string> ReadExpectedSymbols(IEnumerable<string> headerPaths)
    {
        ArgumentNullException.ThrowIfNull(headerPaths);
        SortedSet<string> symbols = new(StringComparer.Ordinal);
        foreach (string path in headerPaths)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Generated public bridge header not found: {path}", path);
            string source = BlockCommentRegex().Replace(LineCommentRegex().Replace(File.ReadAllText(path), string.Empty), string.Empty);
            foreach (Match match in ApiDeclarationRegex().Matches(source))
                symbols.Add(match.Groups[1].Value);
        }

        return symbols.ToArray();
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
            if (trimLeadingUnderscore && symbol.StartsWith('_') && !symbol.StartsWith("__", StringComparison.Ordinal))
                symbol = symbol[1..];
            symbols.Add(symbol);
        }

        return symbols.ToArray();
    }

    private static (string Tool, string[] Arguments, bool TrimLeadingUnderscore) SelectTool(
        string targetIdentifier,
        string libraryPath,
        string? toolPath
    ) {
        if (targetIdentifier.StartsWith("windows-", StringComparison.OrdinalIgnoreCase))
            return (NativeBuildToolDiscovery.FindDumpBin(toolPath) ?? throw new InvalidOperationException("dumpbin was not found. Install Visual Studio C++ build tools or pass --export-tool <dumpbin.exe>."), ["/NOLOGO", "/EXPORTS", libraryPath], false);
        if (targetIdentifier.StartsWith("macos-", StringComparison.OrdinalIgnoreCase) || targetIdentifier.StartsWith("ios-", StringComparison.OrdinalIgnoreCase))
            return (toolPath ?? "nm", ["-gU", libraryPath], true);
        return (toolPath ?? "nm", ["-D", "--defined-only", libraryPath], false);
    }

    private static string Execute(
        string tool,
        IEnumerable<string> arguments,
        string workingDirectory
    ) {
        ProcessExecutionResult result = ProcessExecutor.ExecuteAsync(
            tool, arguments.ToArray(), workingDirectory, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        if (result.timedOut)
            throw new TimeoutException($"Export inspection tool '{tool}' timed out after 30 seconds.");
        if (result.exitCode != 0)
            throw new InvalidOperationException($"Export inspection tool '{tool}' exited with code {result.exitCode}: {result.standardError.Trim()}");
        return result.standardOutput;
    }

    [GeneratedRegex(@"\b(?:[A-Za-z_][A-Za-z0-9_]*)?API(?:_INTERNAL)?\s*\([^;]*?\)\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ApiDeclarationRegex();
    [GeneratedRegex(@"//.*?$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LineCommentRegex();
    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockCommentRegex();
    [GeneratedRegex(@"\b[0-9A-Fa-f]+\s+[A-Za-z]\s+([^\s]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NmExportRegex();
    [GeneratedRegex(@"^\s*\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+([^\s]+)(?:\s*=.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DumpbinExportRegex();
}
