using System;
using System.IO;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;

namespace BGCS.NativeBuild.Planning;

/// <summary>Selects a concrete installed compiler provider without downloading or invoking a shell.</summary>
public sealed class NativeBuildProviderResolver
{
    /// <summary>Resolves the declared driver or selects the installed host compiler.</summary>
    /// <param name="target">The resolved compiler and target facts.</param>
    /// <param name="providerId">The compiler or build-system provider choice.</param>
    /// <param name="buildToolPath">An authoritative optional build-system executable.</param>
    /// <param name="auxiliaryToolPath">An authoritative Ninja executable for CMake/Meson, or an archive tool for direct compiler/native MSBuild providers.</param>
    /// <returns>A caller-owned provider with a resolved executable.</returns>
    public INativeBuildProvider Resolve(
        NativeTargetDescriptor target,
        string providerId,
        string? buildToolPath = null,
        string? auxiliaryToolPath = null
    ) {
        ArgumentNullException.ThrowIfNull(target);
        if (providerId == "msbuild")
        {
            string cl = target.toolchain.compilerPath is string configured ? NativeBuildToolDiscovery.FindExecutable(configured) ?? throw new FileNotFoundException("The configured MSVC driver is unavailable.")
                : MsvcToolchainDiscovery.FindCompiler(target.architectureId) ?? throw new FileNotFoundException("Native MSBuild requires installed Visual C++ tools.");
            return new MsBuildNativeProvider(NativeBuildToolDiscovery.FindMSBuild(buildToolPath) ?? throw new FileNotFoundException("The requested native MSBuild executable is unavailable."),
                MsvcToolchainDiscovery.Resolve(cl, target.architectureId, target.toolchain.sysRoot, target.toolchain.sdkVersion), ResolveArchiveOverride(auxiliaryToolPath));
        }
        if (providerId == "cmake")
            return new CMakeBuildProvider(Resolve(target, "auto"), NativeBuildToolDiscovery.FindCMake(buildToolPath) ?? throw new FileNotFoundException("The requested CMake executable is unavailable."),
                NativeBuildToolDiscovery.FindNinja(auxiliaryToolPath) ?? throw new FileNotFoundException("The requested Ninja executable is unavailable."));
        if (providerId == "meson")
            return new MesonBuildProvider(Resolve(target, "auto"), (buildToolPath is null ? NativeBuildToolDiscovery.FindExecutable("meson") : NativeBuildToolDiscovery.FindExecutable(buildToolPath))
                ?? throw new FileNotFoundException("The requested Meson executable is unavailable."),
                NativeBuildToolDiscovery.FindNinja(auxiliaryToolPath) ?? throw new FileNotFoundException("The requested Ninja executable is unavailable."));
        if (providerId is not ("auto" or "clang" or "clang-cl" or "gnu" or "msvc")) throw new NotSupportedException($"Native provider '{providerId}' is not available through this compiler resolver.");
        if (buildToolPath is not null) throw new ArgumentException("Direct native compiler providers use the target compilerPath; buildToolPath is only meaningful for CMake, Meson or native MSBuild.", nameof(buildToolPath));
        string? archiveOverride = ResolveArchiveOverride(auxiliaryToolPath);
        string? compiler = target.toolchain.compilerPath is string explicitCompiler
            ? NativeBuildToolDiscovery.FindExecutable(explicitCompiler)
            : providerId == "clang" ? NativeBuildToolDiscovery.FindExecutable("clang++") ?? NativeBuildToolDiscovery.FindExecutable("clang++", OperatingSystem.IsWindows() ? "C:/Program Files/LLVM/bin/clang++.exe" : null)
            : providerId == "gnu" ? NativeBuildToolDiscovery.FindExecutable(Environment.GetEnvironmentVariable("BGCS_GNU_CXX") ?? "g++") : providerId == "msvc" ? MsvcToolchainDiscovery.FindCompiler(target.architectureId)
            : providerId == "clang-cl" ? NativeBuildToolDiscovery.FindClangCl() : ToolchainDiscovery.FindCompiler(SourceLanguage.Cpp);
        if (compiler is null) throw new FileNotFoundException("The declared native compiler is unavailable. Configure its executable and the target SDK.");
        if (Path.GetFileName(compiler).Equals("cl.exe", StringComparison.OrdinalIgnoreCase))
        {
            if (providerId is not ("auto" or "msvc")) throw new InvalidOperationException("The native provider does not match the selected MSVC driver.");
            MsvcToolchainInfo resolved = MsvcToolchainDiscovery.Resolve(compiler, target.architectureId, target.toolchain.sysRoot, target.toolchain.sdkVersion);
            if (target.toolchain.compilerVersion is string expectedVersion && resolved.compilerVersion.ToString() != expectedVersion)
                throw new InvalidOperationException("The MSVC compiler version changed between parsing and native construction.");
            if (target.toolchain.linkerPath is string expectedLinker && !Path.GetFullPath(expectedLinker).Equals(resolved.linker, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The MSVC linker changed between parsing and native construction.");
            return new MsvcBuildProvider(resolved, archiveOverride);
        }
        if (providerId == "msvc") throw new InvalidOperationException("The MSVC provider requires a matching cl.exe driver.");
        if (Path.GetFileNameWithoutExtension(compiler).Equals("clang-cl", StringComparison.OrdinalIgnoreCase))
        {
            if (providerId is not ("auto" or "clang-cl")) throw new InvalidOperationException("The native provider does not match the selected clang-cl driver.");
            string cl = target.toolchain.linkerPath is string pinnedLinker ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pinnedLinker))!, "cl.exe")
                : MsvcToolchainDiscovery.FindCompiler(target.architectureId) ?? throw new FileNotFoundException("clang-cl requires a complete matching Visual C++ SDK.");
            MsvcToolchainInfo paired = MsvcToolchainDiscovery.Resolve(cl, target.architectureId, target.toolchain.sysRoot, target.toolchain.sdkVersion);
            if (target.toolchain.linkerPath is string expectedLinker && !Path.GetFullPath(expectedLinker).Equals(paired.linker, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The pinned clang-cl Visual C++ linker changed between parsing and construction.");
            return new ClangClBuildProvider(compiler, paired, archiveOverride);
        }
        if (providerId == "clang-cl") throw new InvalidOperationException("The clang-cl provider requires a matching clang-cl driver.");
        string family = ToolchainDiscovery.GetCompilerDriverFamily(SourceLanguage.Cpp, compiler)
            ?? throw new NotSupportedException("The resolved compiler has no recognized Clang or GNU version identity and requires its own native build provider.");
        if (providerId != "auto" && family != providerId) throw new InvalidOperationException("The native provider does not match the resolved compiler driver.");
        string? archive = archiveOverride ?? NativeBuildToolDiscovery.FindExecutable(Path.Combine(Path.GetDirectoryName(compiler)!, family == "clang" ? OperatingSystem.IsWindows() ? "llvm-ar.exe" : "llvm-ar" : OperatingSystem.IsWindows() ? "ar.exe" : "ar"))
            ?? NativeBuildToolDiscovery.FindExecutable(family == "clang" ? "llvm-ar" : "ar");
        return new CompilerBuildProvider(family, compiler, archive, target.toolchain.linkerPath);
    }

    private static string? ResolveArchiveOverride(string? path) => path is null ? null : NativeBuildToolDiscovery.FindExecutable(path)
        ?? throw new FileNotFoundException("The explicit native archive tool is unavailable; another archive tool cannot replace it.", path);
}
