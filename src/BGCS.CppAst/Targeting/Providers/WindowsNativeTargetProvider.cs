using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BGCS.Core.Targeting;

namespace BGCS.CppAst.Targeting.Providers;

/// <summary>
/// Supplies Windows MSVC targets without depending on the generator's operating system.
/// </summary>
public sealed class WindowsNativeTargetProvider : INativeTargetProvider
{
    /// <inheritdoc/>
    public bool TryResolve(
        NativeTargetRequest request,
        [NotNullWhen(true)] out NativeTargetDescriptor? target
    ) {
        ArgumentNullException.ThrowIfNull(request);
        target = null;
        (string cpu, string[] defines)? definition = request.targetId.value switch
        {
            "windows-x86-msvc" => ("i686", ["_M_IX86=600"]),
            "windows-x64-msvc" => ("x86_64", ["_M_AMD64=100", "_M_X64=100", "_WIN64=1"]),
            "windows-arm64-msvc" => ("aarch64", ["_M_ARM64=1", "_WIN64=1"]),
            _ => null
        };
        if (definition is not { } resolved)
            return false;
        NativeToolchainDescriptor inputs = request.toolchain;
        IEnumerable<string> definitions = new[]
        {
            "_MSC_VER=1930",
            "_WIN32=1"
        }.Concat(resolved.defines).Concat(inputs.defines);
        NativeToolchainDescriptor toolchain = new(inputs.compilerPath, inputs.sysRoot, inputs.systemIncludeFolders,
            definitions, new[] { "-fms-extensions", "-fms-compatibility", "-fms-compatibility-version=19.30" }.Concat(inputs.arguments),
            inputs.cxxSystemIncludeFolders);
        target = new(request.targetId, "windows", request.targetId.value.Split('-')[1], "msvc", request.tripleOverride ?? resolved.cpu + "-pc-windows-msvc", toolchain);
        return true;
    }
}
