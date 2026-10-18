using System;
using System.IO;
using System.Linq;
using BGCS.Contracts.Targets;

namespace BGCS.Platforms.Toolchains;

/// <summary>Resolves explicitly installed Emscripten resources for relocatable Wasm native assets.</summary>
public static class EmscriptenSdkDiscovery
{
    /// <summary>Resolves the SDK's matching Clang driver, sysroot and version without downloads.</summary>
    /// <param name="emsdkRoot">An existing emsdk root containing its upstream installation.</param>
    /// <returns>The wasm32 target with exact installed native SDK resources.</returns>
    public static NativeTargetDescriptor Resolve(string emsdkRoot)
    {
        string upstream = Path.Combine(Path.GetFullPath(emsdkRoot), "upstream"), emscripten = Path.Combine(upstream, "emscripten");
        return ResolveComponents(upstream, emscripten, Path.Combine(emscripten, "cache", "sysroot"));
    }

    /// <summary>Resolves explicitly selected .NET workload SDK and cache tools roots without scanning or choosing another installed version.</summary>
    /// <param name="sdkToolsRoot">The exact restored Emscripten SDK pack tools directory containing bin and emscripten.</param>
    /// <param name="cacheToolsRoot">The matching restored cache pack tools directory containing emscripten/cache/sysroot.</param>
    /// <returns>The immutable wasm32 descriptor pinned to those installed workload resources.</returns>
    public static NativeTargetDescriptor ResolveWorkload(
        string sdkToolsRoot,
        string cacheToolsRoot
    ) {
        string sdk = Path.GetFullPath(sdkToolsRoot), cache = Path.GetFullPath(cacheToolsRoot);
        return ResolveComponents(sdk, Path.Combine(sdk, "emscripten"), Path.Combine(cache, "emscripten", "cache", "sysroot"));
    }

    private static NativeTargetDescriptor ResolveComponents(
        string compilerRoot,
        string emscripten,
        string sysroot
    ) {
        string versionFile = Path.Combine(emscripten, "emscripten-version.txt");
        if (!File.Exists(versionFile)) throw new FileNotFoundException("The Emscripten installation has no version manifest.", versionFile);
        string version = File.ReadAllText(versionFile).Trim().Trim('"');
        if (!Version.TryParse(version, out _)) throw new InvalidDataException("The Emscripten version manifest has an unknown version.");
        string compiler = Path.Combine(compilerRoot, "bin", OperatingSystem.IsWindows() ? "clang++.exe" : "clang++");
        string include = Path.Combine(sysroot, "include"), cxx = Path.Combine(include, "c++", "v1");
        if (!new[] { sysroot, include, cxx }.All(Directory.Exists)) throw new IOException("The selected Emscripten complete parser sysroot must already be installed.");
        NativeToolchainDescriptor toolchain = new(compiler, sysroot, [include], cxxSystemIncludeFolders: [cxx], sdkVersion: version);
        return new(new("emscripten-wasm32-emscripten"), "emscripten", "wasm32", "emscripten", "wasm32-unknown-emscripten", toolchain, byteOrder: BGCS.Contracts.Native.NativeByteOrder.LittleEndian);
    }
}
