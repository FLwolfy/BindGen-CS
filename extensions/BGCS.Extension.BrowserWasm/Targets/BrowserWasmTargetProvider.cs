using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using BGCS.Core.Targets;

namespace BGCS.Extension.BrowserWasm;

/// <summary>
/// Supplies the Emscripten wasm32 ABI independently of the authoring host platform.
/// </summary>
public sealed class BrowserWasmTargetProvider : INativeTargetProvider
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
        string triple = ResolveTriple(request);
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
        target = new(request.targetId, "emscripten", "wasm32", "emscripten", triple, toolchain, capabilities: new(true, false), byteOrder: BGCS.Core.Native.NativeByteOrder.LittleEndian)
        {
            headerSearchPaths = (request.language == BGCS.Core.Inputs.SourceLanguage.Cpp
                ? toolchain.cxxSystemIncludeFolders.Concat(toolchain.systemIncludeFolders)
                : toolchain.systemIncludeFolders).Distinct(StringComparer.Ordinal)
                .Select(path => new BGCS.Core.Targets.HeaderSearchPath(path, false, false)).ToImmutableArray(),
            sourceConventions = new("__attribute__((visibility(\"default\")))", "", new("C", false))
        };
        return true;
    }
    private static string ResolveTriple(NativeTargetRequest request)
    {
        const string expected = "wasm32-unknown-emscripten";
        if (request.tripleOverride is string triple && triple != expected)
            throw new ArgumentException("The explicit triple does not match the Emscripten wasm32 target.", nameof(request));
        return expected;
    }
}
