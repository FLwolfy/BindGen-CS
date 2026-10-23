using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using BGCS.Core.Targets;

namespace BGCS.Targets.Desktop.Targeting.Providers;

/// <summary>
/// Supplies desktop macOS targets independently of optional mobile target extensions.
/// </summary>
public sealed class AppleNativeTargetProvider : INativeTargetProvider
{
    /// <inheritdoc/>
    public bool TryResolve(
        NativeTargetRequest request,
        [NotNullWhen(true)] out NativeTargetDescriptor? target
    ) {
        ArgumentNullException.ThrowIfNull(request);
        target = null;
        (string platform, string architecture, string triple)? definition = request.targetId.value switch
        {
            "macos-x64-darwin" => ("macos", "x64", "x86_64-apple-darwin"),
            "macos-arm64-darwin" => ("macos", "arm64", "arm64-apple-darwin"),
            _ => null
        };
        if (definition is not { } resolved)
            return false;
        string triple = NativeTargetTriple.Resolve(request, resolved.triple, resolved.platform, resolved.architecture, "darwin");
        NativeToolchainDescriptor toolchain = request.toolchain;
        if (OperatingSystem.IsMacOS() && string.IsNullOrWhiteSpace(toolchain.sysRoot)
            && toolchain.compilerPath is null && toolchain.systemIncludeFolders.Count == 0 && toolchain.cxxSystemIncludeFolders.Count == 0)
        {
            string sdk = resolved.platform switch
            {
                _ => "macosx"
            };
            toolchain = new NativeToolchainDescriptor(
                toolchain.compilerPath, BGCS.Targets.Desktop.Toolchains.AppleSdkDiscovery.FindInstalledRoot(sdk),
                toolchain.systemIncludeFolders, toolchain.defines, toolchain.arguments, toolchain.cxxSystemIncludeFolders,
                toolchain.sdkVersion, toolchain.compilerVersion);
        }
        // Complete only paths belonging to this selected SDK; never mix in a
        // different host SDK or libc++ implementation.
        if (!string.IsNullOrWhiteSpace(toolchain.sysRoot))
        {
            string headers = Path.Combine(Path.GetFullPath(toolchain.sysRoot), "usr", "include");
            string cxx = Path.Combine(headers, "c++", "v1");
            toolchain = new(toolchain.compilerPath, toolchain.sysRoot,
                toolchain.systemIncludeFolders.Count == 0 && Directory.Exists(headers) ? [headers] : toolchain.systemIncludeFolders,
                toolchain.defines, toolchain.arguments,
                toolchain.cxxSystemIncludeFolders.Count == 0 && Directory.Exists(cxx) ? [cxx] : toolchain.cxxSystemIncludeFolders,
                toolchain.sdkVersion, toolchain.compilerVersion);
        }

        target = new(request.targetId, resolved.platform, resolved.architecture, "darwin", triple, toolchain, byteOrder: NativeTargetFacts.ByteOrder(resolved.architecture, triple));
        return true;
    }
}
