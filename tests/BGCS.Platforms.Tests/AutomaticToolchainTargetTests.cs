using System.Linq;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.Platforms.Tests;

public sealed class AutomaticToolchainTargetTests
{
    [Fact]
    public void ForeignTargetsRetainOnlyTheirDeclaredParserSdkInputs()
    {
        NativeTargetDescriptor host = new TargetResolver().Resolve(new(new("host")));
        NativeToolchainDescriptor inputs = new(compilerPath: "uninstalled-parser-sdk-hint", systemIncludeFolders: ["declared/include"],
            cxxSystemIncludeFolders: ["declared/cxx"], defines: ["DECLARED=7"], arguments: ["-fno-exceptions"]);
        NativeTargetDescriptor[] targets =
        [
            new(new("foreign-platform"), "foreign", host.architectureId, host.abiId, host.triple, inputs),
            new(new("foreign-cpu"), host.platformId, "foreign", host.abiId, host.triple, inputs),
            new(new("foreign-abi"), host.platformId, host.architectureId, "foreign", host.triple, inputs)
        ];
        foreach (NativeTargetDescriptor target in targets)
        {
            NativeTargetDescriptor resolved = ToolchainResolver.ResolveToolchain(target, SourceLanguage.Cpp);
            Assert.Same(target, resolved);
            Assert.Same(inputs, resolved.toolchain);
            Assert.Equal("uninstalled-parser-sdk-hint", resolved.toolchain.compilerPath);
            Assert.Null(resolved.toolchain.sysRoot);
            Assert.Equal(new[] { "declared/cxx", "declared/include" }, ToolchainResolver.ResolveHeaderSearchPaths(resolved, SourceLanguage.Cpp).Select(path => path.path));
            Assert.Equal(new[] { "declared/include" }, ToolchainResolver.ResolveHeaderSearchPaths(resolved, SourceLanguage.C).Select(path => path.path));
        }
    }

    [Fact]
    public void HostParserSdkResolutionDoesNotRequireOrReplaceAnExecutableHint()
    {
        NativeTargetDescriptor host = new TargetResolver().Resolve(new(new("host"), new(compilerPath: "not-an-installed-compiler")));
        NativeTargetDescriptor resolved = ToolchainResolver.ResolveToolchain(host, SourceLanguage.Cpp);
        Assert.Equal("not-an-installed-compiler", resolved.toolchain.compilerPath);
        Assert.Equal(host.targetId, resolved.targetId);
        Assert.Equal(host.triple, resolved.triple);
    }
}
