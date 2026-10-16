using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;

namespace BGCS.Platforms.Toolchains;

/// <summary>Resolves compiler search facts before requests cross the frontend worker boundary.</summary>
public static class ToolchainResolver
{
    /// <summary>Resolves installed MSVC SDK facts and parser compatibility from the actual selected driver.</summary>
    /// <param name="target">The concrete target and caller inputs.</param>
    /// <param name="language">The native source language.</param>
    /// <param name="driverHint">An optional declared native provider family.</param>
    /// <returns>The immutable target enriched with matching SDK paths.</returns>
    public static NativeTargetDescriptor ResolveToolchain(
        NativeTargetDescriptor target,
        SourceLanguage language,
        string? driverHint = null
    ) {
        ArgumentNullException.ThrowIfNull(target);
        string? compiler = target.toolchain.compilerPath is string explicitCompiler ? ToolchainDiscovery.FindCompiler(language, explicitCompiler)
            : driverHint is "msvc" or "msbuild" ? MsvcToolchainDiscovery.FindCompiler(target.architectureId)
            : driverHint == "clang-cl" ? ToolchainDiscovery.FindCompiler(language, "clang-cl") ?? (File.Exists("C:/Program Files/LLVM/bin/clang-cl.exe") ? "C:/Program Files/LLVM/bin/clang-cl.exe" : null)
            : driverHint == "clang" ? FindClang(language, language == SourceLanguage.Cpp ? "clang++" : "clang")
            : driverHint == "gnu" ? ToolchainDiscovery.FindCompiler(language,
                Environment.GetEnvironmentVariable(language == SourceLanguage.Cpp ? "BGCS_GNU_CXX" : "BGCS_GNU_CC") ?? (language == SourceLanguage.Cpp ? "g++" : "gcc"))
            : driverHint is null or "auto" or "cmake" or "meson" ? ToolchainDiscovery.FindCompiler(language) : null;
        if (driverHint is "msvc" or "msbuild" or "clang-cl" && compiler is null) throw new FileNotFoundException("The requested Windows compiler is unavailable for the native target.");
        if (driverHint == "gnu")
        {
            if (compiler is null) throw new FileNotFoundException("The requested GNU compiler is unavailable; install GCC/G++ or explicitly select the actual compiler family.");
            string? family = ToolchainDiscovery.GetCompilerDriverFamily(language, compiler);
            if (family != "gnu") throw new InvalidOperationException("The requested GNU compiler reports " + (family ?? "an unrecognized implementation")
                + "; a gcc/g++ filename does not prove GNU identity. Select Clang for Apple Clang aliases or configure a genuine GCC/G++ executable.");
        }
        bool clangCl = compiler is not null && Path.GetFileNameWithoutExtension(compiler).Equals("clang-cl", StringComparison.OrdinalIgnoreCase);
        bool clangWindows = compiler is not null && target.platformId == "windows" && target.abiId == "msvc"
            && Path.GetFileNameWithoutExtension(compiler) is "clang" or "clang++";
        if (compiler is null) return target;
        if (!clangCl && !clangWindows && !Path.GetFileName(compiler).Equals("cl.exe", StringComparison.OrdinalIgnoreCase))
        {
            NativeToolchainDescriptor previous = target.toolchain;
            // Keep the exact selected driver across header discovery, parsing and native planning.
            // Content hashes are captured by the host/executor; this operation does not invent a SDK identity.
            NativeToolchainDescriptor pinned = new(compiler, previous.sysRoot, previous.systemIncludeFolders, previous.defines, previous.arguments,
                previous.cxxSystemIncludeFolders, previous.sdkVersion, previous.compilerVersion, previous.linkerPath, previous.libraryDirectories);
            return new(target.targetId, target.platformId, target.architectureId, target.abiId, target.triple, pinned, target.capabilities, target.byteOrder);
        }
        if (target.platformId != "windows" || target.abiId != "msvc") throw new InvalidOperationException("MSVC SDK selection requires a Windows MSVC target.");
        string cl = clangCl || clangWindows ? target.toolchain.linkerPath is string pinnedLinker
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pinnedLinker))!, "cl.exe")
            : MsvcToolchainDiscovery.FindCompiler(target.architectureId) ?? throw new FileNotFoundException("A Windows MSVC-targeting Clang driver requires a matching Visual C++ SDK.") : compiler;
        if (!File.Exists(cl)) throw new FileNotFoundException("The pinned Visual C++ toolset compiler is unavailable.", cl);
        MsvcToolchainInfo sdk = MsvcToolchainDiscovery.Resolve(cl, target.architectureId, target.toolchain.sysRoot, target.toolchain.sdkVersion);
        string compilerVersion = clangCl || clangWindows ? FileVersionInfo.GetVersionInfo(compiler).FileVersion ?? throw new IOException("The selected Clang driver has no file version identity.") : sdk.compilerVersion.ToString();
        if (target.toolchain.compilerVersion is string expectedVersion && compilerVersion != expectedVersion)
            throw new InvalidOperationException("The selected MSVC compiler version changed after toolchain resolution.");
        if (target.toolchain.linkerPath is string expectedLinker && !Path.GetFullPath(expectedLinker).Equals(sdk.linker, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected MSVC linker changed after toolchain resolution.");
        string compatibility = sdk.compilerVersion.Major.ToString(CultureInfo.InvariantCulture) + "." + sdk.compilerVersion.Minor.ToString(CultureInfo.InvariantCulture);
        string macro = "_MSC_VER=" + (sdk.compilerVersion.Major * 100 + sdk.compilerVersion.Minor).ToString(CultureInfo.InvariantCulture);
        NativeToolchainDescriptor original = target.toolchain;
        NativeToolchainDescriptor resolved = new(compiler, sdk.sdkRoot,
            original.systemIncludeFolders.Concat(sdk.includeDirectories).Distinct(StringComparer.OrdinalIgnoreCase), original.defines.Where(define => !define.StartsWith("_MSC_VER=", StringComparison.Ordinal)).Append(macro),
            original.arguments.Where(argument => !argument.StartsWith("-fms-compatibility-version=", StringComparison.Ordinal)).Append("-fms-compatibility-version=" + compatibility), original.cxxSystemIncludeFolders,
            sdk.sdkVersion, compilerVersion, sdk.linker, original.libraryDirectories.Concat(sdk.libraryDirectories).Distinct(StringComparer.OrdinalIgnoreCase));
        return new(target.targetId, target.platformId, target.architectureId, target.abiId, target.triple, resolved, target.capabilities, target.byteOrder);
    }
    private static string? FindClang(
        SourceLanguage language,
        string name
    )
        => ToolchainDiscovery.FindCompiler(language, name) ?? (OperatingSystem.IsWindows()
            ? ToolchainDiscovery.FindCompiler(language, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin", name + ".exe")) : null);
    /// <summary>Discovers host driver headers while preserving explicitly declared cross-target SDK inputs.</summary>
    /// <param name="target">The concrete target and declared toolchain.</param>
    /// <param name="language">The language whose system headers are needed.</param>
    /// <returns>An ordered driver search list; empty when the target does not match the host.</returns>
    public static IReadOnlyList<HeaderSearchPath> ResolveHeaderSearchPaths(
        NativeTargetDescriptor target,
        SourceLanguage language
    ) {
        ArgumentNullException.ThrowIfNull(target);
        if (target.toolchain.compilerPath is string compiler && (Path.GetFileName(compiler).Equals("cl.exe", StringComparison.OrdinalIgnoreCase)
            || target.platformId == "windows" && target.abiId == "msvc" && target.toolchain.sdkVersion is not null))
            return [];
        NativeTargetDescriptor host = new TargetResolver().Resolve(new(new NativeTargetId("host")));
        return target.platformId == host.platformId && target.architectureId == host.architectureId && target.abiId == host.abiId
            ? ToolchainDiscovery.DiscoverHeaderSearchPaths(language, target.toolchain.compilerPath, target.toolchain.sysRoot)
            : [];
    }
}
