using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Core.Execution;
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
    /// <param name = "parserKind">Language parsed by Clang.</param>
    /// <param name = "explicitPath">Optional configured compiler path or executable name.</param>
    /// <returns>An absolute compiler path, or <see langword="null"/> when no compiler can be found.</returns>
    public static string? FindCompiler(
        CppParserKind parserKind,
        string? explicitPath = null
    ) {
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
    /// <param name = "parserKind">Language whose include paths are requested.</param>
    /// <param name = "compilerPath">Optional compiler path or executable name.</param>
    /// <returns>Existing SDK and standard library directories in compiler search order, excluding Clang builtin headers owned by the parser.</returns>
    public static IReadOnlyList<string> DiscoverSystemIncludeFolders(
        CppParserKind parserKind,
        string? compilerPath = null
    ) {
        string? compiler = FindCompiler(parserKind, compilerPath);
        if (compiler == null)
            return [];
        string language = parserKind == CppParserKind.Cpp ? "c++" : parserKind == CppParserKind.ObjC ? "objective-c" : "c";
        string key = GetCompilerFileIdentity(compiler) + "\0" + language;
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
    /// <param name = "parserKind">
    /// The language used when selecting the compiler driver.
    /// </param>
    /// <param name = "compilerPath">
    /// An optional explicit compiler path or executable name.
    /// </param>
    /// <returns>
    /// The resolved driver identity, or a not-found marker. An unsuccessful version query is recorded as unavailable.
    /// </returns>
    /// <remarks>
    /// Compiler queries drain both output streams. A timed-out query is terminated and observed before returning.
    /// </remarks>
    public static string GetCompilerFingerprint(
        CppParserKind parserKind,
        string? compilerPath = null
    ) {
        string? compiler = FindCompiler(parserKind, compilerPath);
        if (compiler == null)
            return "compiler:not-found";
        string key = GetCompilerFileIdentity(compiler);
        lock (Sync)
        {
            if (FingerprintCache.TryGetValue(key, out string? cached))
                return cached;
        }

        var captured = RunForOutput(compiler, ["--version"]);
        string version = captured is { } result ? result.output + result.error : "version:unavailable";
        FileInfo binary = new(compiler);
        string fingerprint = string.Join("\n", "compiler:" + compiler.Replace('\\', '/'), "length:" + binary.Length, "modified-utc:" + binary.LastWriteTimeUtc.Ticks, version.Trim());
        if (key != GetCompilerFileIdentity(compiler))
            throw new IOException($"The compiler changed while its identity was being queried: '{compiler}'.");
        lock (Sync)
            FingerprintCache[key] = fingerprint;
        return fingerprint;
    }

    private static string GetCompilerFileIdentity(string compiler)
    {
        FileInfo binary = new(compiler);
        return compiler + "\0" + binary.Length + "\0" + binary.LastWriteTimeUtc.Ticks;
    }

    /// <summary>
    /// Locates a selected Apple SDK through explicit SDKROOT, xcrun, or the macOS Command Line Tools SDK.
    /// </summary>
    /// <param name = "sdkName">
    /// The xcrun SDK identity, such as macosx, iphoneos, or iphonesimulator.
    /// </param>
    /// <returns>The SDK root path, or <see langword="null"/> when that SDK is unavailable.</returns>
    public static string? FindAppleSdkRoot(string sdkName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sdkName);
        string? configured = Environment.GetEnvironmentVariable("SDKROOT");
        if (IsDirectory(configured))
            return Path.GetFullPath(configured!);
        string? discovered = RunForSingleLine("/usr/bin/xcrun", ["--sdk", sdkName, "--show-sdk-path"]);
        if (IsDirectory(discovered))
            return Path.GetFullPath(discovered!);
        const string commandLineToolsSdk = "/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk";
        return sdkName == "macosx" && Directory.Exists(commandLineToolsSdk) ? commandLineToolsSdk : null;
    }

    private static IReadOnlyList<string> DiscoverSystemIncludeFoldersCore(
        string compiler,
        string language
    ) {
        try
        {
            string? resourceRoot = RunForSingleLine(compiler, ["-print-resource-dir"]);
            string? resourceInclude = string.IsNullOrWhiteSpace(resourceRoot) ? null : Path.GetFullPath(Path.Combine(resourceRoot, "include"));
            StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var captured = RunForOutput(compiler, ["-E", "-x", language, "-", "-v"], 10_000);
            if (captured is not { } result)
                return [];
            string output = result.error;
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
                if (!Directory.Exists(line))
                    continue;
                string fullPath = Path.GetFullPath(line);
                if (!pathComparer.Equals(fullPath, resourceInclude) && !paths.Contains(fullPath, pathComparer))
                    paths.Add(fullPath);
            }

            return paths;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string?> GetCompilerCandidates(
        CppParserKind parserKind,
        string? explicitPath
    ) {
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

    private static string? RunForSingleLine(
        string executable,
        IReadOnlyList<string> arguments
    ) {
        var captured = RunForOutput(executable, arguments);
        return captured?.output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }

    private static (string output, string error)? RunForOutput(
        string executable,
        IReadOnlyList<string> arguments,
        int timeoutMilliseconds = 5_000
    ) {
        try
        {
            ProcessExecutionResult result = ProcessExecutor.ExecuteAsync(
                executable, arguments, Environment.CurrentDirectory,
                TimeSpan.FromMilliseconds(timeoutMilliseconds)).GetAwaiter().GetResult();
            return !result.timedOut && result.exitCode == 0 ? (result.standardOutput, result.standardError) : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsDirectory(string? path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
}
