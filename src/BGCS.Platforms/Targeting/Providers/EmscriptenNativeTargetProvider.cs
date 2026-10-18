using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using BGCS.Contracts.Targets;

namespace BGCS.Platforms.Targeting.Providers;

/// <summary>
/// Supplies the Emscripten wasm32 ABI independently of the authoring host platform.
/// </summary>
public sealed class EmscriptenNativeTargetProvider : INativeTargetProvider
{
    /// <inheritdoc/>
    public bool TryResolve(
        NativeTargetRequest request,
        [NotNullWhen(true)] out NativeTargetDescriptor? target
    ) {
        ArgumentNullException.ThrowIfNull(request);
        target = null;
        if (request.targetId.value != "emscripten-wasm32-emscripten")
            return false;
        string triple = NativeTargetTriple.Resolve(request, "wasm32-unknown-emscripten", "emscripten", "wasm32", "emscripten");
        NativeToolchainDescriptor toolchain = request.toolchain;
        if (!string.IsNullOrWhiteSpace(toolchain.sysRoot))
        {
            string includes = Path.Combine(Path.GetFullPath(toolchain.sysRoot), "include");
            string cxxIncludes = Path.Combine(includes, "c++", "v1");
            toolchain = new(toolchain.compilerPath, toolchain.sysRoot,
                toolchain.systemIncludeFolders.Append(includes).Distinct(StringComparer.Ordinal),
                toolchain.defines, toolchain.arguments,
                Directory.Exists(cxxIncludes)
                    ? toolchain.cxxSystemIncludeFolders.Append(cxxIncludes).Distinct(StringComparer.Ordinal)
                    : toolchain.cxxSystemIncludeFolders,
                toolchain.sdkVersion, toolchain.compilerVersion);
        }
        target = new(request.targetId, "emscripten", "wasm32", "emscripten", triple, toolchain, byteOrder: NativeTargetFacts.ByteOrder("wasm32", triple));
        return true;
    }
}
