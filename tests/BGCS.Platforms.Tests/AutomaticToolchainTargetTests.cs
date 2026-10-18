using System.Linq;
using System;
using System.Runtime.InteropServices;
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
        Assert.Same(host, resolved);
        Assert.Empty(resolved.toolchain.systemIncludeFolders);
        Assert.Empty(resolved.toolchain.cxxSystemIncludeFolders);
    }

    [Theory]
    [InlineData("sysroot")]
    [InlineData("c")]
    [InlineData("cpp")]
    public void ExplicitHostSdkInputsNeverAcquireFallbackHeadersOrDefinitions(string choice)
    {
        NativeTargetDescriptor host = new TargetResolver().Resolve(new(new("host")));
        NativeToolchainDescriptor inputs = new(sysRoot: choice == "sysroot" ? "declared/sysroot" : null,
            systemIncludeFolders: choice == "c" ? ["declared/c"] : [],
            cxxSystemIncludeFolders: choice == "cpp" ? ["declared/cpp"] : [],
            defines: ["DECLARED=7"], arguments: ["-DONLY_THIS_SDK=1"]);
        NativeTargetDescriptor selected = new(host.targetId, host.platformId, host.architectureId, host.abiId, host.triple, inputs);
        NativeTargetDescriptor resolved = ToolchainResolver.ResolveToolchain(selected, SourceLanguage.Cpp);
        Assert.Same(selected, resolved);
        Assert.Same(inputs, resolved.toolchain);
        Assert.Equal(new[] { "DECLARED=7" }, resolved.toolchain.defines);
        Assert.Equal(new[] { "-DONLY_THIS_SDK=1" }, resolved.toolchain.arguments);
    }

    [Theory]
    [InlineData("gnu", "x86_64-unknown-linux-gnu")]
    [InlineData("musl", "x86_64-unknown-linux-musl")]
    public void LinuxLibcIdentitiesRemainDistinctForExplicitTargets(string abi, string triple)
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("linux-x64-" + abi), new(systemIncludeFolders: ["explicit/sdk"])));
        Assert.Equal(abi, target.abiId);
        Assert.Equal(triple, target.triple);
        Assert.Same(target, ToolchainResolver.ResolveToolchain(target, SourceLanguage.C));
    }

    [Fact]
    public void LinuxHostAliasRequiresItsRuntimeToEstablishTheLibcIdentity()
    {
        if (!OperatingSystem.IsLinux()) return;
        string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string rid = RuntimeInformation.RuntimeIdentifier;
        if (rid != "linux-" + architecture && rid != "linux-musl-" + architecture)
        {
            Assert.Throws<PlatformNotSupportedException>(() => new TargetResolver().Resolve(new(new("host"))));
            return;
        }
        NativeTargetDescriptor host = new TargetResolver().Resolve(new(new("host")));
        Assert.Equal(rid.StartsWith("linux-musl-", StringComparison.Ordinal) ? "musl" : "gnu", host.abiId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownPlatformRequiresDeclaredCapabilitiesAndKeepsRuntimeRestrictions(bool capabilities)
    {
        NativeTargetDescriptor selected = new(new("private-parser"), "private-platform", "x64", "msvc", "x86_64-pc-windows-msvc", new(),
            capabilities ? new(false, true) : null);
        TargetResolver resolver = new([new PrivateProvider(selected)]);
        if (!capabilities)
        {
            Assert.Contains("capability", Assert.Throws<NotSupportedException>(() => resolver.Resolve(new(selected.targetId))).Message);
            return;
        }
        NativeTargetDescriptor target = resolver.Resolve(new(selected.targetId));
        Assert.Equal(ManagedRuntimeKind.CoreClr, ManagedRuntimeResolver.Resolve(target, ManagedRuntimeKind.CoreClr).kind);
        Assert.Null(ManagedRuntimeResolver.Resolve(target, ManagedRuntimeKind.CoreClr).aggregateAbi);
        Assert.Throws<NotSupportedException>(() => ManagedRuntimeResolver.Resolve(target, ManagedRuntimeKind.BrowserWasm));
    }

    private sealed class PrivateProvider(NativeTargetDescriptor target) : INativeTargetProvider
    {
        public bool TryResolve(NativeTargetRequest request, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NativeTargetDescriptor? result)
        {
            result = request.targetId == target.targetId ? target : null;
            return result is not null;
        }
    }
}
