using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.CppAst.Parsing;

namespace BGCS.CppAst.Targeting;

/// <summary>
/// Discovers host C/C++ compiler drivers, SDK roots, and compiler-provided system include directories.
/// </summary>
public static class CppToolchainDiscovery
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, IReadOnlyList<string>> IncludeCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> FingerprintCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Locates a compiler driver for the requested language on the current host.
    /// </summary>
    /// <param name="parserKind">Language parsed by Clang.</param>
    /// <param name="explicitPath">Optional configured compiler path or executable name.</param>
    /// <returns>An absolute compiler path, or <see langword="null"/> when no compiler can be found.</returns>
    public static string? FindCompiler(CppParserKind parserKind, string? explicitPath = null)
    {
        foreach (string? candidate in GetCompilerCandidates(parserKind, explicitPath))
        {
            string? resolved = ResolveExecutable(candidate);
            if (resolved != null)
                return resolved;
        }
        return null;
    }

    /// <summary>
    /// Asks the host compiler driver for the system include paths it would use.
    /// </summary>
    /// <param name="parserKind">Language whose include paths are requested.</param>
    /// <param name="compilerPath">Optional compiler path or executable name.</param>
    /// <returns>Existing include directories in compiler search order.</returns>
    public static IReadOnlyList<string> DiscoverSystemIncludeFolders(CppParserKind parserKind, string? compilerPath = null)
    {
        string? compiler = FindCompiler(parserKind, compilerPath);
        if (compiler == null)
            return [];
        string language = parserKind == CppParserKind.Cpp ? "c++" : parserKind == CppParserKind.ObjC ? "objective-c" : "c";
        string key = compiler + "\0" + language;
        lock (Sync)
        {
            if (IncludeCache.TryGetValue(key, out IReadOnlyList<string>? cached))
                return cached;
        }

        IReadOnlyList<string> discovered = DiscoverSystemIncludeFoldersCore(compiler, language);
        lock (Sync)
            IncludeCache[key] = discovered;
        return discovered;
    }

    /// <summary>
    /// Returns a stable compiler-driver identity for incremental generation keys.
    /// The identity includes the resolved path, binary metadata, and complete <c>--version</c> output.
    /// </summary>
    public static string GetCompilerFingerprint(CppParserKind parserKind, string? compilerPath = null)
    {
        string? compiler = FindCompiler(parserKind, compilerPath);
        if (compiler == null)
            return "compiler:not-found";
        lock (Sync)
        {
            if (FingerprintCache.TryGetValue(compiler, out string? cached))
                return cached;
        }

        string version = RunForOutput(compiler, ["--version"]) ?? "version:unavailable";
        FileInfo binary = new(compiler);
        string fingerprint = string.Join("\n",
            "compiler:" + compiler.Replace('\\', '/'),
            "length:" + binary.Length,
            "modified-utc:" + binary.LastWriteTimeUtc.Ticks,
            version.Trim());
        lock (Sync)
            FingerprintCache[compiler] = fingerprint;
        return fingerprint;
    }

    /// <summary>
    /// Locates the active macOS SDK through <c>SDKROOT</c>, <c>xcrun</c>, or Command Line Tools.
    /// </summary>
    /// <returns>The SDK root path, or <see langword="null"/> when no SDK is installed.</returns>
    public static string? FindMacOsSdkRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("SDKROOT");
        if (IsDirectory(configured))
            return Path.GetFullPath(configured!);

        string? discovered = RunForSingleLine("/usr/bin/xcrun", ["--sdk", "macosx", "--show-sdk-path"]);
        if (IsDirectory(discovered))
            return Path.GetFullPath(discovered!);

        const string commandLineToolsSdk = "/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk";
        return Directory.Exists(commandLineToolsSdk) ? commandLineToolsSdk : null;
    }

    private static IReadOnlyList<string> DiscoverSystemIncludeFoldersCore(string compiler, string language)
    {
        try
        {
            ProcessStartInfo start = new(compiler)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string argument in new[] { "-E", "-x", language, "-", "-v" })
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            process.StandardInput.Close();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return [];
            }
            string output = standardError.GetAwaiter().GetResult();
            _ = standardOutput.GetAwaiter().GetResult();
            bool capture = false;
            List<string> paths = [];
            foreach (string rawLine in output.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Contains("#include <...> search starts here:", StringComparison.Ordinal))
                {
                    capture = true;
                    continue;
                }
                if (capture && line.StartsWith("End of search list.", StringComparison.Ordinal))
                    break;
                if (!capture || line.Length == 0)
                    continue;
                const string frameworkSuffix = " (framework directory)";
                if (line.EndsWith(frameworkSuffix, StringComparison.Ordinal))
                    line = line[..^frameworkSuffix.Length].TrimEnd();
                if (Directory.Exists(line) && !paths.Contains(line, StringComparer.Ordinal))
                    paths.Add(Path.GetFullPath(line));
            }
            return paths;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string?> GetCompilerCandidates(CppParserKind parserKind, string? explicitPath)
    {
        yield return explicitPath;
        if (parserKind == CppParserKind.Cpp)
        {
            yield return Environment.GetEnvironmentVariable("BGCS_CPP2C_CXX");
            yield return Environment.GetEnvironmentVariable("CXX");
        }
        else
        {
            yield return Environment.GetEnvironmentVariable("BGCS_CC");
            yield return Environment.GetEnvironmentVariable("CC");
        }
        if (OperatingSystem.IsWindows())
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "LLVM", "bin", parserKind == CppParserKind.Cpp ? "clang++.exe" : "clang.exe");
        }
        if (OperatingSystem.IsMacOS())
            yield return parserKind == CppParserKind.Cpp ? "/usr/bin/clang++" : "/usr/bin/clang";
        yield return parserKind == CppParserKind.Cpp ? "clang++" : "clang";
        yield return parserKind == CppParserKind.Cpp ? "g++" : "gcc";
        yield return parserKind == CppParserKind.Cpp ? "c++" : "cc";
    }

    private static string? ResolveExecutable(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;
        string value = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
        if (Path.IsPathRooted(value) || value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(value) ? Path.GetFullPath(value) : null;
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;
        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string fullPath = Path.Combine(directory, value);
            if (File.Exists(fullPath))
                return Path.GetFullPath(fullPath);
            if (OperatingSystem.IsWindows() && !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string executablePath = fullPath + ".exe";
                if (File.Exists(executablePath))
                    return Path.GetFullPath(executablePath);
            }
        }
        return null;
    }

    private static string? RunForSingleLine(string executable, IReadOnlyList<string> arguments)
    {
        if (!File.Exists(executable))
            return null;
        try
        {
            ProcessStartInfo start = new(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string argument in arguments)
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5_000) || process.ExitCode != 0)
                return null;
            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? RunForOutput(string executable, IReadOnlyList<string> arguments)
    {
        try
        {
            ProcessStartInfo start = new(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string argument in arguments)
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5_000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            string output = standardOutput.GetAwaiter().GetResult();
            string error = standardError.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output + error : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsDirectory(string? path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
}
