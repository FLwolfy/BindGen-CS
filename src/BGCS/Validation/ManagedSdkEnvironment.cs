using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace BGCS.Validation;

internal sealed record ManagedSdkSelection(
    string executable,
    string sdkVersion,
    IReadOnlyDictionary<string, string?> environment
);

internal static class ManagedSdkEnvironment
{
    internal static ManagedSdkSelection Resolve()
    {
        string executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        List<string> candidates = [];
        Add(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"));
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is string configuredRoot) Add(Path.Combine(configuredRoot, executableName));
        string? runtimeRoot = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent?.Parent?.Parent?.FullName;
        if (runtimeRoot is not null) Add(Path.Combine(runtimeRoot, executableName));
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(directory)) Add(Path.Combine(directory.Trim('"'), executableName));
        foreach (string candidate in candidates.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            if (!File.Exists(candidate)) continue;
            string physical = new FileInfo(candidate).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate;
            string root = Path.GetDirectoryName(physical)!;
            string sdk = Path.Combine(root, "sdk");
            if (!Directory.Exists(sdk)) continue;
            string? version = Directory.GetDirectories(sdk).Select(Path.GetFileName)
                .Where(name => Version.TryParse(name, out Version? parsed) && parsed.Major == 10)
                .OrderByDescending(name => Version.Parse(name!)).FirstOrDefault();
            if (version is null) continue;
            // Keep distinct Unix environment spellings so every ambient alias is
            // removed from the private managed verifier, without changing its caller.
            Dictionary<string, string?> environment = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (DictionaryEntry item in Environment.GetEnvironmentVariables())
            {
                string name = (string)item.Key;
                if (name.Equals("Platform", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("DOTNET_MSBUILD_SDK_RESOLVER_", StringComparison.OrdinalIgnoreCase)) environment[name] = null;
            }
            environment["DOTNET_ROOT"] = root;
            environment["DOTNET_HOST_PATH"] = physical;
            return new(physical, version, environment);
        }
        throw new FileNotFoundException("Managed compilation requires an installed .NET 10 SDK. Set DOTNET_HOST_PATH or DOTNET_ROOT to that installation.");

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value)) candidates.Add(Path.GetFullPath(value));
        }
    }
}
