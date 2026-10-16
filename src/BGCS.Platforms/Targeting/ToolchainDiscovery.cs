using BGCS.Contracts.Targets;
using BGCS.Contracts.Inputs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using System.Security.Cryptography;
using BGCS.Infrastructure.Execution;

namespace BGCS.Platforms.Targeting;

/// <summary>
/// Discovers host C/C++ compiler drivers, SDK roots, and compiler-provided system include directories.
/// </summary>
public static class ToolchainDiscovery
{
    private static readonly ImmutableDictionary<string, string?> s_queryEnvironment =
        new[] { "CL", "_CL_", "LINK", "_LINK_", "LIB", "INCLUDE", "CPATH", "C_INCLUDE_PATH", "CPLUS_INCLUDE_PATH", "OBJC_INCLUDE_PATH", "LIBRARY_PATH", "COMPILER_PATH", "GCC_EXEC_PREFIX", "CCC_OVERRIDE_OPTIONS" }
        .ToImmutableDictionary(name => name, _ => (string?)null, StringComparer.Ordinal);
    /// <summary>
    /// Locates a compiler driver for the requested language on the current host.
    /// </summary>
    /// <param name = "parserKind">Language parsed by Clang.</param>
    /// <param name = "explicitPath">Optional configured compiler path or executable name.</param>
    /// <returns>An absolute compiler path, or <see langword="null"/> when no compiler can be found.</returns>
    public static string? FindCompiler(
        SourceLanguage parserKind,
        string? explicitPath = null
    ) {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return ResolveExecutable(explicitPath);
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
    /// <param name = "sysRoot">Optional SDK root passed to the driver so discovered paths belong to the selected target SDK.</param>
    /// <returns>Existing SDK and standard library directories in compiler search order, excluding Clang builtin headers owned by the parser.</returns>
    public static IReadOnlyList<string> DiscoverSystemIncludeFolders(
        SourceLanguage parserKind,
        string? compilerPath = null,
        string? sysRoot = null
    ) {
        return DiscoverHeaderSearchPaths(parserKind, compilerPath, sysRoot)
            .Where(entry => !entry.isBuiltin).Select(entry => entry.path).ToArray();
    }

    /// <summary>Returns discovered header paths and their search roles without loading a parser.</summary>
    /// <param name="parserKind">The selected source language.</param>
    /// <param name="compilerPath">An explicit driver path, or null for discovery.</param>
    /// <param name="sysRoot">The target SDK root, or null for the driver's default.</param>
    /// <returns>A read-only search-order snapshot including identified builtin and framework roots.</returns>
    public static IReadOnlyList<HeaderSearchPath> DiscoverHeaderSearchPaths(
        SourceLanguage parserKind,
        string? compilerPath,
        string? sysRoot
    ) {
        string? compiler = FindCompiler(parserKind, compilerPath);
        if (compiler == null)
            return [];
        string language = parserKind == SourceLanguage.Cpp ? "c++" : parserKind == SourceLanguage.ObjC ? "objective-c" : "c";
        string? root = string.IsNullOrWhiteSpace(sysRoot) ? null : Path.GetFullPath(sysRoot);
        // Search results can depend on environment and SDK installation changes outside the driver.
        // A process-wide path/mtime cache cannot prove those inputs unchanged.
        return DiscoverSystemIncludeFoldersCore(compiler, language, root);
    }

    /// <summary>
    /// Returns a stable compiler-driver identity for incremental generation keys.
    /// The identity includes the resolved path, binary SHA-256, metadata, and complete <c>--version</c> output.
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
        SourceLanguage parserKind,
        string? compilerPath = null
    ) {
        string? compiler = FindCompiler(parserKind, compilerPath);
        if (compiler == null)
            return "compiler:not-found";
        string key = GetCompilerFileIdentity(compiler);
        var captured = RunForOutput(compiler, IsClangDriver(compiler) ? ["--no-default-config", "--version"] : ["--version"]);
        string version = captured is { } result ? result.output + result.error : "version:unavailable";
        FileInfo binary = new(compiler);
        string fingerprint = string.Join("\n", "compiler:" + compiler.Replace('\\', '/'), "length:" + binary.Length, "modified-utc:" + binary.LastWriteTimeUtc.Ticks,
            "sha256:" + key[(key.LastIndexOf('\0') + 1)..], version.Trim());
        if (key != GetCompilerFileIdentity(compiler))
            throw new IOException($"The compiler changed while its identity was being queried: '{compiler}'.");
        return fingerprint;
    }

    /// <summary>Identifies a GNU-style driver's reported implementation independently of its executable filename.</summary>
    /// <param name="parserKind">The language used for executable discovery.</param>
    /// <param name="compilerPath">The selected driver path or executable name.</param>
    /// <returns>Clang or GNU as a lowercase stable identity, or null for unavailable or unrecognized version output.</returns>
    /// <remarks>A successful bounded version query identifies the driver family; it does not certify target ABI or compilation behavior.</remarks>
    public static string? GetCompilerDriverFamily(
        SourceLanguage parserKind,
        string compilerPath
    ) {
        string? compiler = FindCompiler(parserKind, compilerPath);
        if (compiler is null) return null;
        string identity = GetCompilerFileIdentity(compiler);
        var captured = RunForOutput(compiler, IsClangDriver(compiler) ? ["--no-default-config", "--version"] : ["--version"]);
        if (identity != GetCompilerFileIdentity(compiler)) throw new IOException("The native compiler changed while its implementation identity was being queried.");
        if (captured is not { } result) return null;
        string version = result.output + "\n" + result.error;
        if (version.Contains("clang version", StringComparison.OrdinalIgnoreCase)) return "clang";
        if (version.Contains("Free Software Foundation", StringComparison.Ordinal)
            && (version.Contains("gcc", StringComparison.OrdinalIgnoreCase) || version.Contains("g++", StringComparison.OrdinalIgnoreCase))) return "gnu";
        return null;
    }

    private static string GetCompilerFileIdentity(string compiler)
    {
        FileInfo binary = new(compiler);
        using FileStream stream = File.OpenRead(compiler);
        return compiler + "\0" + binary.Length + "\0" + binary.LastWriteTimeUtc.Ticks + "\0" + Convert.ToHexStringLower(SHA256.HashData(stream));
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

    private static IReadOnlyList<HeaderSearchPath> DiscoverSystemIncludeFoldersCore(
        string compiler,
        string language,
        string? sysRoot
    ) {
        try
        {
            bool clang = IsClangDriver(compiler);
            string? resourceRoot = RunForSingleLine(compiler, clang ? ["--no-default-config", "-print-resource-dir"] : ["-print-resource-dir"]);
            string? resourceInclude = string.IsNullOrWhiteSpace(resourceRoot) ? null : Path.GetFullPath(Path.Combine(resourceRoot, "include"));
            StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            List<string> arguments = ["-E", "-x", language, "-", "-v"];
            if (clang) arguments.Insert(0, "--no-default-config");
            if (sysRoot is not null)
            {
                arguments.Add("--sysroot=" + sysRoot);
                arguments.Add("-isysroot");
                arguments.Add(sysRoot);
            }
            var captured = RunForOutput(compiler, arguments, 10_000);
            if (captured is not { } result)
                return [];
            string output = result.error;
            bool capture = false;
            List<HeaderSearchPath> paths = [];
            HashSet<string> visited = new(pathComparer);
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
                bool isFramework = line.EndsWith(frameworkSuffix, StringComparison.Ordinal);
                if (isFramework)
                    line = line[..^frameworkSuffix.Length].TrimEnd();
                if (!Directory.Exists(line))
                    continue;
                string fullPath = Path.GetFullPath(line);
                if (visited.Add(fullPath))
                    paths.Add(new(fullPath, pathComparer.Equals(fullPath, resourceInclude), isFramework));
            }

            return paths.ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string?> GetCompilerCandidates(
        SourceLanguage parserKind,
        string? explicitPath
    ) {
        yield return explicitPath;
        if (parserKind == SourceLanguage.Cpp)
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
            yield return Path.Combine(programFiles, "LLVM", "bin", parserKind == SourceLanguage.Cpp ? "clang++.exe" : "clang.exe");
        }

        if (OperatingSystem.IsMacOS())
            yield return parserKind == SourceLanguage.Cpp ? "/usr/bin/clang++" : "/usr/bin/clang";
        yield return parserKind == SourceLanguage.Cpp ? "clang++" : "clang";
        yield return parserKind == SourceLanguage.Cpp ? "g++" : "gcc";
        yield return parserKind == SourceLanguage.Cpp ? "c++" : "cc";
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
                TimeSpan.FromMilliseconds(timeoutMilliseconds), environmentOverrides: s_queryEnvironment).GetAwaiter().GetResult();
            return !result.timedOut && result.exitCode == 0 ? (result.standardOutput, result.standardError) : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsDirectory(string? path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
    private static bool IsClangDriver(string path) => Path.GetFileNameWithoutExtension(path).Contains("clang", StringComparison.OrdinalIgnoreCase);
}
