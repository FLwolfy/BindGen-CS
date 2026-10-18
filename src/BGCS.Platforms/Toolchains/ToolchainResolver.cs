using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;

namespace BGCS.Platforms.Toolchains;

/// <summary>Reads installed SDK headers and environment metadata without executing native tools.</summary>
public static class ToolchainResolver
{
    /// <summary>Enriches the requested parser target with read-only host SDK paths.</summary>
    /// <param name="target">The exact requested target and explicit SDK inputs.</param>
    /// <param name="language">The source language whose headers are needed.</param>
    /// <returns>A detached target; foreign targets retain only their explicit SDK inputs.</returns>
    public static NativeTargetDescriptor ResolveToolchain(
        NativeTargetDescriptor target,
        SourceLanguage language
    ) {
        ArgumentNullException.ThrowIfNull(target);
        NativeToolchainDescriptor original = target.toolchain;
        // Explicit SDK roots are the complete selected parser environment, even
        // when its ABI happens to match the generator host.
        if (original.sysRoot is not null || original.systemIncludeFolders.Count != 0 || original.cxxSystemIncludeFolders.Count != 0)
            return target;
        // A declared installation hint never authorizes selecting another
        // compiler's SDK. Only the implemented exact MSVC layout is enriched.
        if (original.compilerPath is string declaredCompiler && (!OperatingSystem.IsWindows()
            || !Path.IsPathFullyQualified(declaredCompiler) || !File.Exists(declaredCompiler)
            || !Path.GetFileName(declaredCompiler).Equals("cl.exe", StringComparison.OrdinalIgnoreCase))) return target;
        NativeTargetDescriptor host;
        try { host = new TargetResolver().Resolve(new(new("host"))); }
        catch (PlatformNotSupportedException) { return target; }
        if (target.platformId != host.platformId || target.architectureId != host.architectureId || target.abiId != host.abiId) return target;
        List<string> includes = [.. original.systemIncludeFolders], cxx = [.. original.cxxSystemIncludeFolders];
        List<string> defines = [.. original.defines], arguments = [.. original.arguments];
        string? compiler = original.compilerPath, sysroot = original.sysRoot, sdkVersion = original.sdkVersion, compilerVersion = original.compilerVersion;
        if (OperatingSystem.IsWindows())
        {
            string? installed = compiler is not null && File.Exists(compiler) && Path.GetFileName(compiler).Equals("cl.exe", StringComparison.OrdinalIgnoreCase)
                ? compiler : MsvcToolchainDiscovery.FindCompiler(target.architectureId);
            if (installed is not null)
            {
                MsvcToolchainInfo sdk = MsvcToolchainDiscovery.Resolve(installed, target.architectureId, sysroot, sdkVersion);
                includes.AddRange(sdk.includeDirectories);
                sysroot = sdk.sdkRoot;
                sdkVersion = sdk.sdkVersion;
                compilerVersion ??= sdk.compilerVersion.ToString();
                defines.RemoveAll(value => value.StartsWith("_MSC_VER=", StringComparison.Ordinal));
                defines.Add("_MSC_VER=" + (sdk.compilerVersion.Major * 100 + sdk.compilerVersion.Minor));
                arguments.RemoveAll(value => value.StartsWith("-fms-compatibility-version=", StringComparison.Ordinal));
                arguments.Add("-fms-compatibility-version=" + sdk.compilerVersion.Major + "." + sdk.compilerVersion.Minor);
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            sysroot ??= AppleSdkDiscovery.FindInstalledRoot("macosx");
            if (sysroot is not null) Add(includes, Path.Combine(sysroot, "usr", "include"));
            // Current Apple SDKs carry libc++ themselves; do not substitute a
            // different developer installation for the selected SDK's headers.
            if (cxx.Count == 0 && sysroot is not null) Add(cxx, Path.Combine(sysroot, "usr", "include", "c++", "v1"));
            if (cxx.Count == 0)
                foreach (string path in new[] { "/Library/Developer/CommandLineTools/usr/include/c++/v1", "/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/include/c++/v1" })
                {
                    Add(cxx, path);
                    if (cxx.Count != 0) break;
                }
        }
        else if (OperatingSystem.IsLinux() && sysroot is null)
        {
            Add(includes, "/usr/local/include");
            string architecture = target.architectureId switch { "x64" => "x86_64-linux-" + target.abiId, "arm64" => "aarch64-linux-" + target.abiId, _ => target.triple };
            Add(includes, "/usr/include/" + architecture);
            Add(includes, "/usr/include");
            if (Directory.Exists("/usr/include/c++"))
            {
                string? selected = Directory.GetDirectories("/usr/include/c++").OrderByDescending(path => Version.TryParse(Path.GetFileName(path), out Version? value) ? value : new Version()).FirstOrDefault();
                if (selected is not null)
                {
                    Add(cxx, selected);
                    Add(cxx, "/usr/include/" + architecture + "/c++/" + Path.GetFileName(selected));
                    Add(cxx, Path.Combine(selected, "backward"));
                }
            }
        }
        NativeToolchainDescriptor resolved = new(compiler, sysroot, includes.Distinct(StringComparer.Ordinal), defines, arguments,
            cxx.Distinct(StringComparer.Ordinal), sdkVersion, compilerVersion);
        return new(target.targetId, target.platformId, target.architectureId, target.abiId, target.triple, resolved, target.capabilities, target.byteOrder);

        static void Add(
            List<string> paths,
            string path
        ) { if (Directory.Exists(path)) paths.Add(Path.GetFullPath(path)); }
    }

    /// <summary>Returns the already resolved ordered parser SDK directories without querying a compiler.</summary>
    /// <param name="target">The immutable parser target.</param>
    /// <param name="language">The selected source language.</param>
    /// <returns>Explicit SDK paths, preserving C and C++ separation.</returns>
    public static IReadOnlyList<HeaderSearchPath> ResolveHeaderSearchPaths(
        NativeTargetDescriptor target,
        SourceLanguage language
    )
        => (language == SourceLanguage.Cpp ? target.toolchain.cxxSystemIncludeFolders.Concat(target.toolchain.systemIncludeFolders) : target.toolchain.systemIncludeFolders)
            .Distinct(StringComparer.Ordinal).Select(path => new HeaderSearchPath(path, false, false)).ToArray();
}
