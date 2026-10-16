using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BGCS.Infrastructure.Execution;
using Microsoft.Win32;

namespace BGCS.Platforms.Toolchains;

/// <summary>Describes explicit Visual C++ and Windows SDK paths without a developer shell.</summary>
/// <param name="compiler">The target-specific compiler executable.</param>
/// <param name="linker">The matching linker executable.</param>
/// <param name="compilerVersion">The compiler's structured file version.</param>
/// <param name="sdkRoot">The selected Windows SDK root.</param>
/// <param name="sdkVersion">The selected Windows SDK version.</param>
/// <param name="includeDirectories">VC, UCRT, shared and UM header paths in search order.</param>
/// <param name="libraryDirectories">Target-specific VC, UCRT and UM library paths.</param>
public sealed record MsvcToolchainInfo(
    string compiler,
    string linker,
    Version compilerVersion,
    string sdkRoot,
    string sdkVersion,
    ImmutableArray<string> includeDirectories,
    ImmutableArray<string> libraryDirectories
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
        string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere)) return null;
        ProcessExecutionResult query = ProcessExecutor.ExecuteAsync(vswhere,
            ["-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"],
            Path.GetDirectoryName(vswhere)!, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        if (query.timedOut || query.exitCode != 0) throw new IOException("Visual C++ installation discovery failed: " + query.standardError);
        string? installation = query.standardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (installation is null) return null;
        string tools = Path.Combine(installation, "VC", "Tools", "MSVC");
        if (!Directory.Exists(tools)) return null;
        foreach (string root in Directory.GetDirectories(tools).OrderByDescending(path => Version.TryParse(Path.GetFileName(path), out Version? version) ? version : new Version(0, 0)))
        {
            string compiler = Path.Combine(root, "bin", host, architecture, "cl.exe");
            if (File.Exists(compiler)) return Path.GetFullPath(compiler);
        }
        return null;
    }

    /// <summary>Resolves matching compiler, headers and linker libraries for an installed toolchain.</summary>
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
        string linker = Path.Combine(target.FullName, "link.exe");
        string vcHeaders = Path.Combine(vc, "include"), vcLibraries = Path.Combine(vc, "lib", architecture);
        if (!File.Exists(linker) || !Directory.Exists(vcHeaders) || !Directory.Exists(vcLibraries)) throw new IOException("The selected Visual C++ installation is incomplete.");
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
            string ucrtLib = Path.Combine(root, "Lib", version, "ucrt", architecture), umLib = Path.Combine(root, "Lib", version, "um", architecture);
            if (!new[] { ucrt, shared, um, ucrtLib, umLib }.All(Directory.Exists)) continue;
            FileVersionInfo metadata = FileVersionInfo.GetVersionInfo(absolute);
            return new(absolute, linker, new(metadata.FileMajorPart, metadata.FileMinorPart, metadata.FileBuildPart, metadata.FilePrivatePart), root, version,
                [vcHeaders, ucrt, shared, um], [vcLibraries, ucrtLib, umLib]);
        }
        throw new IOException("No complete Windows SDK headers and linker libraries exist for " + architecture + (sdkVersion is null ? "." : " at the pinned version " + sdkVersion + "."));
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
