using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BGCS.Core.Execution;

namespace BGCS.Cpp2C.Build;

/// <summary>Locates optional native build tools without requiring a developer command prompt.</summary>
public static class NativeBuildToolDiscovery
{
    /// <summary>
    /// Resolves an explicit candidate first, then searches the requested command on PATH.
    /// </summary>
    /// <param name="executable">Command name to locate.</param>
    /// <param name="explicitPath">Optional candidate checked before PATH discovery.</param>
    /// <returns>An absolute executable path, or null when neither candidate can be resolved.</returns>
    public static string? FindExecutable(
        string executable,
        string? explicitPath = null
    ) {
        foreach (string? candidate in new[]
        {
            explicitPath,
            executable
        }

        )
        {
            string? resolved = ResolveExecutable(candidate);
            if (resolved != null)
                return resolved;
        }

        return null;
    }

    /// <summary>
    /// Locates clang-cl through the supplied candidate, PATH, LLVM installation or Visual Studio.
    /// </summary>
    /// <param name="explicitPath">Optional clang-cl candidate.</param>
    /// <returns>An absolute compiler path, or null when clang-cl is unavailable.</returns>
    public static string? FindClangCl(string? explicitPath = null)
    {
        string? resolved = FindExecutable("clang-cl", explicitPath);
        if (resolved != null)
            return resolved;
        if (!OperatingSystem.IsWindows())
            return null;
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        resolved = ResolveExecutable(Path.Combine(programFiles, "LLVM", "bin", "clang-cl.exe"));
        if (resolved != null)
            return resolved;
        string? visualStudio = FindVisualStudioInstallation();
        return visualStudio == null ? null : ResolveExecutable(Path.Combine(visualStudio, "VC", "Tools", "Llvm", "x64", "bin", "clang-cl.exe"));
    }

    /// <summary>
    /// Locates MSBuild through an explicit candidate, Visual Studio, environment or PATH.
    /// </summary>
    /// <param name="explicitPath">Authoritative optional candidate; an unresolved explicit candidate returns null.</param>
    /// <returns>An absolute MSBuild path, or null when the requested tool is unavailable.</returns>
    public static string? FindMSBuild(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return ResolveExecutable(explicitPath);
        if (OperatingSystem.IsWindows())
        {
            string? visualStudio = FindVisualStudioInstallation();
            string? current = visualStudio == null ? null : ResolveExecutable(Path.Combine(visualStudio, "MSBuild", "Current", "Bin", "MSBuild.exe"));
            if (current != null)
                return current;
        }

        return FindExecutable("msbuild", Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH"));
    }

    /// <summary>
    /// Locates dumpbin through the supplied candidate, PATH or installed Visual Studio C++ tools.
    /// </summary>
    /// <param name="explicitPath">Optional dumpbin candidate.</param>
    /// <returns>An absolute export inspection tool path, or null when it is unavailable.</returns>
    public static string? FindDumpBin(string? explicitPath = null)
    {
        string? resolved = FindExecutable("dumpbin", explicitPath);
        if (resolved != null || !OperatingSystem.IsWindows())
            return resolved;
        string? visualStudio = FindVisualStudioInstallation();
        string toolsRoot = visualStudio == null ? string.Empty : Path.Combine(visualStudio, "VC", "Tools", "MSVC");
        if (!Directory.Exists(toolsRoot))
            return null;
        string target = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        foreach (string version in Directory.GetDirectories(toolsRoot).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
        {
            resolved = ResolveExecutable(Path.Combine(version, "bin", "Hostx64", target, "dumpbin.exe"));
            if (resolved != null)
                return resolved;
        }

        return null;
    }

    private static string? FindVisualStudioInstallation()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string vswhere = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
            return null;
        try
        {
            ProcessExecutionResult result = ProcessExecutor.ExecuteAsync(
                vswhere,
                ["-latest", "-products", "*", "-requires", "Microsoft.Component.MSBuild", "-property", "installationPath"],
                Environment.CurrentDirectory,
                TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            if (result.timedOut || result.exitCode != 0)
                return null;
            string output = result.standardOutput;
            string? installation = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return Directory.Exists(installation) ? Path.GetFullPath(installation) : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ResolveExecutable(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;
        string value = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
        if (Path.IsPathRooted(value) || value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(value) ? Path.GetFullPath(value) : null;
        string? searchPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(searchPath))
            return null;
        foreach (string directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string path = Path.Combine(directory, value);
            if (File.Exists(path))
                return Path.GetFullPath(path);
            if (OperatingSystem.IsWindows() && !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path + ".exe"))
                return Path.GetFullPath(path + ".exe");
        }

        return null;
    }
}
