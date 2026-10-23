using System;
using System.IO;
using System.Linq;
using BGCS.Core.Inputs;
using BGCS.Core.Targets;
using BGCS.Targets.Desktop.Targeting;
using BGCS.Targets.Desktop.Toolchains;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ReadOnlyToolchainTests
{
    [Fact]
    public void DeclaredSdkOrderKeepsCppWrappersSeparateFromCHeaders()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "bgcs-readonly-sdk"));
        string c = Path.Combine(root, "c"), cpp = Path.Combine(root, "cpp");
        NativeTargetDescriptor target = new(new("foreign-parser"), "foreign", "x64", "foreign", "x86_64-unknown-linux-gnu",
            new(compilerPath: "uninstalled-compiler", systemIncludeFolders: [c], cxxSystemIncludeFolders: [cpp]));
        NativeTargetDescriptor resolved = ToolchainResolver.ResolveToolchain(target, SourceLanguage.Cpp);
        Assert.Same(target, resolved);
        Assert.Equal(new[] { cpp, c }, ToolchainResolver.ResolveHeaderSearchPaths(resolved, SourceLanguage.Cpp).Select(path => path.path));
        Assert.Equal(new[] { c }, ToolchainResolver.ResolveHeaderSearchPaths(resolved, SourceLanguage.C).Select(path => path.path));
        Assert.All(ToolchainResolver.ResolveHeaderSearchPaths(resolved, SourceLanguage.Cpp), path => Assert.False(path.isBuiltin));
    }

    [Fact]
    public void HostResolutionPreservesAnUninstalledHintAndDeclaredVersion()
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("host"), new(compilerPath: "not-an-installed-native-driver", compilerVersion: "declared-version")));
        NativeTargetDescriptor first = ToolchainResolver.ResolveToolchain(target, SourceLanguage.Cpp);
        NativeTargetDescriptor second = ToolchainResolver.ResolveToolchain(target, SourceLanguage.Cpp);
        Assert.Equal(target.toolchain.compilerPath, first.toolchain.compilerPath);
        Assert.Equal("declared-version", first.toolchain.compilerVersion);
        Assert.Equal(first.triple, second.triple);
        Assert.True(first.toolchain.systemIncludeFolders.SequenceEqual(second.toolchain.systemIncludeFolders));
        Assert.True(first.toolchain.cxxSystemIncludeFolders.SequenceEqual(second.toolchain.cxxSystemIncludeFolders));
    }
}
