using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using BGCS.Core.Targeting;

namespace BGCS.CppAst.Targeting.Providers;

/// <summary>
/// Distinguishes macOS, iOS devices and iOS simulators at the target ABI boundary.
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
            "ios-arm64-darwin" => ("ios", "arm64", "arm64-apple-ios"),
            "ios-simulator-x64-darwin" => ("ios-simulator", "x64", "x86_64-apple-ios-simulator"),
            "ios-simulator-arm64-darwin" => ("ios-simulator", "arm64", "arm64-apple-ios-simulator"),
            _ => null
        };
        if (definition is not { } resolved)
            return false;
        NativeToolchainDescriptor toolchain = request.toolchain;
        if (OperatingSystem.IsMacOS() && string.IsNullOrWhiteSpace(toolchain.sysRoot))
        {
            string sdk = resolved.platform switch
            {
                "ios" => "iphoneos",
                "ios-simulator" => "iphonesimulator",
                _ => "macosx"
            };
            toolchain = new NativeToolchainDescriptor(
                toolchain.compilerPath, CppToolchainDiscovery.FindAppleSdkRoot(sdk),
                toolchain.systemIncludeFolders, toolchain.defines, toolchain.arguments, toolchain.cxxSystemIncludeFolders);
        }
        if (!string.IsNullOrWhiteSpace(toolchain.sysRoot))
        {
            string headers = Path.Combine(Path.GetFullPath(toolchain.sysRoot), "usr", "include", "c++", "v1");
            if (Directory.Exists(headers))
                toolchain = new(toolchain.compilerPath, toolchain.sysRoot, toolchain.systemIncludeFolders,
                    toolchain.defines, toolchain.arguments, toolchain.cxxSystemIncludeFolders.Append(headers).Distinct(StringComparer.Ordinal));
        }

        target = new(request.targetId, resolved.platform, resolved.architecture, "darwin", request.tripleOverride ?? resolved.triple, toolchain);
        return true;
    }
}
