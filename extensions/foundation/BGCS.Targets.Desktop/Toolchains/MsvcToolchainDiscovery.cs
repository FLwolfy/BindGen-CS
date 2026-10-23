using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BGCS.Targets.Desktop.Toolchains;

/// <summary>Describes explicit Visual C++ and Windows SDK paths without a developer shell.</summary>
/// <param name="compiler">The target-specific compiler executable.</param>
/// <param name="compilerVersion">The compiler's structured file version.</param>
/// <param name="sdkRoot">The selected Windows SDK root.</param>
/// <param name="sdkVersion">The selected Windows SDK version.</param>
/// <param name="includeDirectories">VC, UCRT, shared and UM header paths in search order.</param>
public sealed record MsvcToolchainInfo(
    string compiler,
    Version compilerVersion,
    string sdkRoot,
    string sdkVersion,
    ImmutableArray<string> includeDirectories
);

/// <summary>Locates installed Visual C++ tools and a complete Windows SDK without downloading or invoking a command shell.</summary>
public static class MsvcToolchainDiscovery
{
    /// <summary>Locates a compiler for the requested Windows target architecture.</summary>
    /// <param name="architecture">x64, x86 or arm64.</param>
    /// <returns>An existing absolute compiler path, or null when the required tools are unavailable.</returns>
    public static string? FindCompiler(string architecture)
    {
        if (!OperatingSystem.IsWindows()) return null;
        ValidateArchitecture(architecture);
        string host = RuntimeInformation.ProcessArchitecture switch { Architecture.Arm64 => "Hostarm64", Architecture.X86 => "Hostx86", _ => "Hostx64" };
        string? configured = Environment.GetEnvironmentVariable("VCToolsInstallDir");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string compiler = Path.Combine(configured, "bin", host, architecture, "cl.exe");
            if (File.Exists(compiler)) return Path.GetFullPath(compiler);
        }
        // Installed metadata and environment hints are sufficient for parser SDK discovery.
        // Do not launch vswhere, a compiler or a command shell from the generator.
        foreach (string programFiles in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct())
        {
            string installations = Path.Combine(programFiles, "Microsoft Visual Studio");
            if (!Directory.Exists(installations)) continue;
            foreach (string version in Directory.GetDirectories(installations).OrderDescending(StringComparer.Ordinal))
                foreach (string edition in Directory.GetDirectories(version).Order(StringComparer.Ordinal))
                {
                    string tools = Path.Combine(edition, "VC", "Tools", "MSVC");
                    if (!Directory.Exists(tools)) continue;
                    foreach (string toolset in Directory.GetDirectories(tools).OrderByDescending(path => Version.TryParse(Path.GetFileName(path), out Version? value) ? value : new Version()))
                    {
                        string compiler = Path.Combine(toolset, "bin", host, architecture, "cl.exe");
                        if (File.Exists(compiler)) return Path.GetFullPath(compiler);
                    }
                }
        }
        return null;
    }

    /// <summary>Resolves matching installed parser headers for an installed toolchain.</summary>
    /// <param name="compiler">An absolute cl.exe path; it must name the requested architecture.</param>
    /// <param name="architecture">x64, x86 or arm64.</param>
    /// <param name="sdkRoot">An explicit SDK root, or null for installed Windows SDK discovery.</param>
    /// <param name="sdkVersion">An exact SDK version, or null to select and freeze the latest complete version.</param>
    /// <returns>The complete immutable toolchain paths.</returns>
    public static MsvcToolchainInfo Resolve(
        string compiler,
        string architecture,
        string? sdkRoot = null,
        string? sdkVersion = null
    ) {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Installed MSVC SDK discovery requires a Windows host.");
        ValidateArchitecture(architecture);
        string absolute = Path.GetFullPath(compiler);
        if (!File.Exists(absolute) || !Path.GetFileName(absolute).Equals("cl.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("MSVC requires an existing absolute cl.exe driver.", absolute);
        DirectoryInfo target = new(Path.GetDirectoryName(absolute)!);
        if (!target.Name.Equals(architecture, StringComparison.OrdinalIgnoreCase) || target.Parent?.Parent?.Name != "bin")
            throw new InvalidOperationException("The MSVC driver directory does not match the declared target architecture.");
        string vc = target.Parent.Parent.Parent!.FullName;
        string vcHeaders = Path.Combine(vc, "include");
        if (!Directory.Exists(vcHeaders)) throw new IOException("The selected Visual C++ installation is incomplete.");
        string root = Path.GetFullPath(sdkRoot ?? InstalledSdkRoot());
        string includes = Path.Combine(root, "Include");
        if (!Directory.Exists(includes)) throw new DirectoryNotFoundException("The Windows SDK root has no Include directory: " + root);
        if (sdkVersion is not null && (!Version.TryParse(sdkVersion, out _) || sdkVersion.Contains('/') || sdkVersion.Contains('\\')))
            throw new ArgumentException("The Windows SDK version must be an exact numeric version.", nameof(sdkVersion));
        string[] versionRoots = sdkVersion is null ? Directory.GetDirectories(includes) : [Path.Combine(includes, sdkVersion)];
        foreach (string versionRoot in versionRoots.OrderByDescending(path => Version.TryParse(Path.GetFileName(path), out Version? version) ? version : new Version(0, 0)))
        {
            string version = Path.GetFileName(versionRoot);
            string ucrt = Path.Combine(versionRoot, "ucrt"), shared = Path.Combine(versionRoot, "shared"), um = Path.Combine(versionRoot, "um");
            if (!new[] { ucrt, shared, um }.All(Directory.Exists)) continue;
            FileVersionInfo metadata = FileVersionInfo.GetVersionInfo(absolute);
            return new(absolute, new(metadata.FileMajorPart, metadata.FileMinorPart, metadata.FileBuildPart, metadata.FilePrivatePart), root, version,
                [vcHeaders, ucrt, shared, um]);
        }
        throw new IOException("No complete Windows SDK headers exist for " + architecture + (sdkVersion is null ? "." : " at the pinned version " + sdkVersion + "."));
    }

    [SupportedOSPlatform("windows")]
    private static string InstalledSdkRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("WindowsSdkDir");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return configured;
        string? installed = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots", "KitsRoot10", null) as string;
        return installed ?? throw new DirectoryNotFoundException("No Windows SDK is configured or installed.");
    }

    private static void ValidateArchitecture(string architecture)
    {
        if (architecture is not ("x64" or "x86" or "arm64")) throw new NotSupportedException("MSVC target architecture must be x64, x86 or arm64.");
    }
}
