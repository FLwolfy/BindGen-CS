using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Result of comparing generated bridge declarations with a native library export table.
/// </summary>
public sealed record NativeExportInspectionResult(
    string Tool,
    IReadOnlyList<string> Expected,
    IReadOnlyList<string> Actual,
    IReadOnlyList<string> Missing)
{
    public bool Success => Missing.Count == 0;
}

/// <summary>
/// Reads expected symbols from generated public headers and verifies the built binary export table.
/// </summary>
public static partial class NativeExportInspector
{
    public static NativeExportInspectionResult Inspect(
        CppBridgeBuildManifest manifest,
        string manifestPath,
        string libraryPath,
        string? toolPath = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        string root = NativeBuildPaths.GetManifestDirectory(manifestPath);
        string[] expected = ReadExpectedSymbols(manifest.PublicHeaderFiles.Select(path => NativeBuildPaths.Resolve(root, path))).ToArray();
        (string tool, string[] arguments, bool trimLeadingUnderscore) = SelectTool(manifest.TargetIdentifier, libraryPath, toolPath);
        string output = Execute(tool, arguments, root);
        string[] actual = ParseExports(output, trimLeadingUnderscore).ToArray();
        string[] missing = expected.Except(actual, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return new(tool, expected, actual, missing);
    }

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

    internal static IReadOnlyList<string> ParseExports(string output, bool trimLeadingUnderscore)
    {
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
        string? toolPath)
    {
        if (targetIdentifier.StartsWith("windows-", StringComparison.OrdinalIgnoreCase))
            return (NativeBuildToolDiscovery.FindDumpBin(toolPath)
                    ?? throw new InvalidOperationException("dumpbin was not found. Install Visual Studio C++ build tools or pass --export-tool <dumpbin.exe>."),
                ["/NOLOGO", "/EXPORTS", libraryPath], false);
        if (targetIdentifier.StartsWith("macos-", StringComparison.OrdinalIgnoreCase) ||
            targetIdentifier.StartsWith("ios-", StringComparison.OrdinalIgnoreCase))
            return (toolPath ?? "nm", ["-gU", libraryPath], true);
        return (toolPath ?? "nm", ["-D", "--defined-only", libraryPath], false);
    }

    private static string Execute(string tool, IEnumerable<string> arguments, string workingDirectory)
    {
        ProcessStartInfo startInfo = new(tool)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start export inspection tool '{tool}'.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Export inspection tool '{tool}' timed out after 30 seconds.");
        }
        string output = standardOutput.GetAwaiter().GetResult();
        string error = standardError.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Export inspection tool '{tool}' exited with code {process.ExitCode}: {error.Trim()}");
        return output;
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
