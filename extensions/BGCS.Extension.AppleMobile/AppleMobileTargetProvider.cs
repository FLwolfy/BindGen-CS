using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;

namespace BGCS.Extension.AppleMobile;

/// <summary>Keeps Apple mobile device and simulator SDK identities separate from desktop SDK defaults.</summary>
public sealed class AppleMobileTargetProvider : INativeTargetProvider
{
    /// <inheritdoc/>
    public bool TryResolve(
        NativeTargetRequest request,
        [NotNullWhen(true)] out NativeTargetDescriptor? target
    ) {
        ArgumentNullException.ThrowIfNull(request);
        target = null;
        (string platform, string architecture, string cpu)? definition = request.targetId.value switch
        {
            "ios-arm64-darwin" => ("ios", "arm64", "arm64"),
            "ios-simulator-arm64-darwin" => ("ios-simulator", "arm64", "arm64"),
            "ios-simulator-x64-darwin" => ("ios-simulator", "x64", "x86_64"),
            _ => null
        };
        if (definition is not { } selected) return false;
        bool simulator = selected.platform == "ios-simulator";
        string suffix = simulator ? "-simulator" : "";
        string triple = request.tripleOverride ?? selected.cpu + "-apple-ios" + suffix;
        string cpuPattern = selected.architecture == "arm64" ? "(?:arm64|aarch64)" : "x86_64";
        if (!Regex.IsMatch(triple, "\\A" + cpuPattern + "-apple-ios(?:[0-9]+(?:\\.[0-9]+){0,3})?" + suffix + "\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            throw new ArgumentException("The explicit triple does not match the selected Apple mobile device/simulator ABI.", nameof(request));
        NativeToolchainDescriptor original = request.toolchain;
        string? root = original.sysRoot;
        // Discovery is limited to an undeclared native Apple environment; explicit roots never change.
        if (root is null && original.compilerPath is null && original.systemIncludeFolders.Count == 0 && original.cxxSystemIncludeFolders.Count == 0 && OperatingSystem.IsMacOS())
            root = AppleSdkDiscovery.FindInstalledRoot(simulator ? "iphonesimulator" : "iphoneos");
        List<string> includes = [.. original.systemIncludeFolders], cxx = [.. original.cxxSystemIncludeFolders];
        List<HeaderSearchPath> frameworks = [];
        if (root is not null)
        {
            root = Path.GetFullPath(root);
            if (includes.Count == 0) Add(includes, Path.Combine(root, "usr", "include"));
            if (cxx.Count == 0) Add(cxx, Path.Combine(root, "usr", "include", "c++", "v1"));
            foreach (string relative in new[] { "System/Library/Frameworks", "System/Library/SubFrameworks", "Library/Frameworks" })
            {
                string path = Path.GetFullPath(Path.Combine(root, relative));
                if (Directory.Exists(path)) frameworks.Add(new(path, false, true));
            }
        }
        NativeToolchainDescriptor toolchain = new(original.compilerPath, root, includes, original.defines, original.arguments, cxx, original.sdkVersion, original.compilerVersion);
        target = new(request.targetId, selected.platform, selected.architecture, "darwin", triple, toolchain, new(true, false), NativeByteOrder.LittleEndian)
        {
            headerSearchPaths = (request.language == SourceLanguage.Cpp ? cxx.Concat(includes) : includes).Distinct(StringComparer.Ordinal).Select(path => new HeaderSearchPath(path, false, false)).Concat(frameworks).ToImmutableArray(),
            sourceConventions = new("__attribute__((visibility(\"default\")))", "", new(selected.architecture == "x64" ? "X86_64SysV" : "C", false))
        };
        return true;
    }
    private static void Add(
        List<string> paths,
        string path
    ) { if (Directory.Exists(path)) paths.Add(Path.GetFullPath(path)); }
}
