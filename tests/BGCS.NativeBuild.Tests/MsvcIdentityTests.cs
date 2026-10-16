using System;
using System.IO;
using System.Linq;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class MsvcIdentityTests
{
    [Fact]
    public void PinnedSdkVersionDoesNotFollowNewerInstallation()
    {
        if (!OperatingSystem.IsWindows()) return;
        string compiler = MsvcToolchainDiscovery.FindCompiler("x64") ?? throw new InvalidOperationException("The Windows native gate requires Visual C++ tools.");
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-sdk-pin-" + Guid.NewGuid().ToString("N"));
        try
        {
            CreateSdk(temporary, "10.0.22621.0");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("windows-x64-msvc"), new(compiler, temporary)));
            NativeTargetDescriptor resolved = ToolchainResolver.ResolveToolchain(target, SourceLanguage.Cpp);
            Assert.Equal("10.0.22621.0", resolved.toolchain.sdkVersion);
            Assert.NotNull(resolved.toolchain.compilerVersion);
            Assert.NotNull(resolved.toolchain.linkerPath);
            Assert.Contains(resolved.toolchain.libraryDirectories, path => path.Contains("10.0.22621.0", StringComparison.Ordinal));
            CreateSdk(temporary, "10.0.26100.0");
            NativeTargetDescriptor again = ToolchainResolver.ResolveToolchain(resolved, SourceLanguage.Cpp);
            Assert.Equal(resolved.toolchain.sdkVersion, again.toolchain.sdkVersion);
            Assert.DoesNotContain(again.toolchain.systemIncludeFolders, path => path.Contains("10.0.26100.0", StringComparison.Ordinal));
            Assert.Equal("10.0.26100.0", MsvcToolchainDiscovery.Resolve(compiler, "x64", temporary).sdkVersion);
            Assert.IsType<MsvcBuildProvider>(new NativeBuildProviderResolver().Resolve(again, "msvc"));
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void MissingPinnedSdkAndChangedCompilerIdentityAreRejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        string compiler = MsvcToolchainDiscovery.FindCompiler("x64") ?? throw new InvalidOperationException("The Windows native gate requires Visual C++ tools.");
        MsvcToolchainInfo installed = MsvcToolchainDiscovery.Resolve(compiler, "x64");
        Assert.Throws<IOException>(() => MsvcToolchainDiscovery.Resolve(compiler, "x64", installed.sdkRoot, "10.0.1.0"));
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("windows-x64-msvc"),
            new(compiler, installed.sdkRoot, sdkVersion: installed.sdkVersion, compilerVersion: "0.0.0.0")));
        Assert.Throws<InvalidOperationException>(() => ToolchainResolver.ResolveToolchain(target, SourceLanguage.Cpp));
        Assert.Throws<InvalidOperationException>(() => new NativeBuildProviderResolver().Resolve(target, "msvc"));
        Assert.Throws<ArgumentException>(() => MsvcToolchainDiscovery.Resolve(compiler, "x64", installed.sdkRoot, "../10.0.1.0"));
    }

    [Fact]
    public void TargetProvidersPreserveFrozenToolchainIdentity()
    {
        string[] libraries = [Path.GetFullPath("sdk-library")];
        NativeToolchainDescriptor original = new(sdkVersion: "10.0.22621.0", compilerVersion: "19.44.0.0", linkerPath: "link.exe", libraryDirectories: libraries);
        libraries[0] = "changed";
        foreach (string id in new[] { "windows-x64-msvc", "macos-x64-darwin", "emscripten-wasm32-emscripten", "linux-x64-gnu" })
        {
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new(id), original));
            Assert.Equal(original.sdkVersion, target.toolchain.sdkVersion);
            Assert.Equal(original.compilerVersion, target.toolchain.compilerVersion);
            Assert.Equal(original.linkerPath, target.toolchain.linkerPath);
            Assert.Equal(original.libraryDirectories.ToArray(), target.toolchain.libraryDirectories.ToArray());
        }
    }

    private static void CreateSdk(string root, string version)
    {
        foreach (string section in new[] { "ucrt", "shared", "um" }) Directory.CreateDirectory(Path.Combine(root, "Include", version, section));
        foreach (string section in new[] { "ucrt", "um" }) Directory.CreateDirectory(Path.Combine(root, "Lib", version, section, "x64"));
    }
}
