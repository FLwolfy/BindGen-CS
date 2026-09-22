namespace BGCS.Tool;

internal static class DotNetHostDiscovery
{
    public static string FindOrThrow()
    {
        foreach (string? candidate in GetCandidates())
        {
            string? resolved = ResolveExecutable(candidate);
            if (resolved != null)
                return resolved;
        }

        throw new FileNotFoundException(
            "The .NET host could not be found. Set DOTNET_HOST_PATH or add dotnet to PATH.");
    }

    private static IEnumerable<string?> GetCandidates()
    {
        yield return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

        string executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string? processPath = Environment.ProcessPath;
        if (string.Equals(Path.GetFileName(processPath), executableName, StringComparison.OrdinalIgnoreCase))
            yield return processPath;

        string? dotNetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotNetRoot))
            yield return Path.Combine(dotNetRoot, executableName);
        string? dotNetRootX86 = Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)");
        if (!string.IsNullOrWhiteSpace(dotNetRootX86))
            yield return Path.Combine(dotNetRootX86, executableName);

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            yield return Path.Combine(userProfile, ".dotnet", executableName);

        if (OperatingSystem.IsWindows())
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
                yield return Path.Combine(programFiles, "dotnet", executableName);
        }
        else
        {
            yield return "/usr/local/share/dotnet/dotnet";
            yield return "/usr/share/dotnet/dotnet";
        }

        yield return executableName;
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
        }
        return null;
    }
}
