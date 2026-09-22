using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BGCS.Cpp2C.Build;

/// <summary>Locates optional native build tools without requiring a developer command prompt.</summary>
public static class NativeBuildToolDiscovery
{
    public static string? FindExecutable(string executable, string? explicitPath = null)
    {
        foreach (string? candidate in new[] { explicitPath, executable })
        {
            string? resolved = ResolveExecutable(candidate);
            if (resolved != null)
                return resolved;
        }
        return null;
    }

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

    public static string? FindMSBuild(string? explicitPath = null)
    {
        string? resolved = FindExecutable("msbuild", explicitPath ?? Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH"));
        if (resolved != null || !OperatingSystem.IsWindows())
            return resolved;
        string? visualStudio = FindVisualStudioInstallation();
        return visualStudio == null ? null : ResolveExecutable(Path.Combine(visualStudio, "MSBuild", "Current", "Bin", "MSBuild.exe"));
    }

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
            ProcessStartInfo start = new(vswhere)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string argument in new[] { "-latest", "-products", "*", "-requires", "Microsoft.Component.MSBuild", "-property", "installationPath" })
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5_000) || process.ExitCode != 0)
                return null;
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
