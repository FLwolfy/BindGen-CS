using System;
using System.IO;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.Platforms.Tests;

public sealed class AutomaticToolchainTargetTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("auto")]
    [InlineData("cmake")]
    [InlineData("meson")]
    public void AutomaticDiscoveryDoesNotInventForeignCompilerOrSdkInputs(string? provider)
    {
        NativeTargetDescriptor host = new TargetResolver().Resolve(new(new NativeTargetId("host")));
        NativeToolchainDescriptor inputs = new(systemIncludeFolders: ["declared/include"], cxxSystemIncludeFolders: ["declared/cxx"],
            defines: ["DECLARED=7"], arguments: ["-fno-exceptions"], libraryDirectories: ["declared/lib"]);
        NativeTargetDescriptor[] foreignTargets =
        [
            new(new("test-foreign-platform"), "foreign", host.architectureId, host.abiId, host.triple, inputs),
            new(new("test-foreign-cpu"), host.platformId, "foreign", host.abiId, host.triple, inputs),
            new(new("test-foreign-abi"), host.platformId, host.architectureId, "foreign", host.triple, inputs)
        ];

        foreach (NativeTargetDescriptor declared in foreignTargets)
        {
            NativeTargetDescriptor resolved = ToolchainResolver.ResolveToolchain(declared, SourceLanguage.Cpp, provider);
            Assert.Same(declared, resolved);
            Assert.Same(inputs, resolved.toolchain);
            Assert.Null(resolved.toolchain.compilerPath);
            Assert.Null(resolved.toolchain.sysRoot);
            Assert.Null(resolved.toolchain.sdkVersion);
            Assert.Null(resolved.toolchain.linkerPath);
            Assert.Equal(["declared/include"], resolved.toolchain.systemIncludeFolders);
            Assert.Equal(["declared/cxx"], resolved.toolchain.cxxSystemIncludeFolders);
            Assert.Equal(["declared/lib"], resolved.toolchain.libraryDirectories);
            Assert.Empty(ToolchainResolver.ResolveHeaderSearchPaths(resolved, SourceLanguage.Cpp));
        }
    }

    [Fact]
    public void HostAutomaticDiscoveryStillSelectsTheInstalledCompiler()
    {
        string expected = ToolchainDiscovery.FindCompiler(SourceLanguage.Cpp)
            ?? throw new InvalidOperationException("The desktop platform matrix requires an installed C++ compiler.");
        NativeTargetDescriptor host = new TargetResolver().Resolve(new(new NativeTargetId("host")));

        NativeTargetDescriptor resolved = ToolchainResolver.ResolveToolchain(host, SourceLanguage.Cpp);

        Assert.Equal(expected, resolved.toolchain.compilerPath);
        Assert.Equal(host.targetId, resolved.targetId);
        Assert.Equal(host.triple, resolved.triple);
    }

    [Fact]
    public void ForeignAutomaticModeRetainsAnExplicitCompilerSelection()
    {
        string driver = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin", "clang++.exe") : "clang++";
        string compiler = ToolchainDiscovery.FindCompiler(SourceLanguage.Cpp, driver)
            ?? throw new InvalidOperationException("The desktop platform matrix requires an installed Clang C++ compiler.");
        NativeTargetDescriptor declared = new TargetResolver().Resolve(new(new NativeTargetId("emscripten-wasm32-emscripten"), new(compiler)));

        NativeTargetDescriptor resolved = ToolchainResolver.ResolveToolchain(declared, SourceLanguage.Cpp, "auto");

        Assert.Equal(compiler, resolved.toolchain.compilerPath);
        Assert.Equal(declared.targetId, resolved.targetId);
        Assert.Equal(declared.triple, resolved.triple);
        Assert.Null(resolved.toolchain.sysRoot);
    }
}
