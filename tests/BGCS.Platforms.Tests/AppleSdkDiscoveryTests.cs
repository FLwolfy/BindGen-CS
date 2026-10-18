using System;
using System.IO;
using System.Linq;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.Platforms.Tests;

[CollectionDefinition("Apple SDK environment", DisableParallelization = true)]
public sealed class AppleSdkEnvironmentCollection;

[Collection("Apple SDK environment")]
public sealed class AppleSdkDiscoveryTests
{
    [Theory]
    [InlineData("clt")]
    [InlineData("xcode")]
    [InlineData("bundle")]
    public void DeclaredDeveloperSelectsItsSdkAndKeepsCppHeadersSeparate(string layout)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-apple-sdk-" + Guid.NewGuid().ToString("N"));
        string developer = layout == "clt" ? Path.Combine(root, "CommandLineTools")
            : Path.Combine(root, "Xcode_16.4.app", "Contents", "Developer");
        string sdk = CreateSdk(layout == "clt" ? Path.Combine(developer, "SDKs")
            : Path.Combine(developer, "Platforms", "MacOSX.platform", "Developer", "SDKs"));
        string? previousSdk = Environment.GetEnvironmentVariable("SDKROOT"), previousDeveloper = Environment.GetEnvironmentVariable("DEVELOPER_DIR");
        try
        {
            Environment.SetEnvironmentVariable("SDKROOT", null);
            Environment.SetEnvironmentVariable("DEVELOPER_DIR", layout == "bundle" ? Path.Combine(root, "Xcode_16.4.app") : developer);
            Assert.Equal(sdk, AppleSdkDiscovery.FindInstalledRoot("macosx"));
            NativeTargetDescriptor target = AppleSdkDiscovery.Resolve(new("macos-x64-darwin"), sdk, "uninstalled-readonly-hint");
            string c = Path.Combine(sdk, "usr", "include"), cpp = Path.Combine(c, "c++", "v1");
            Assert.Equal("uninstalled-readonly-hint", target.toolchain.compilerPath);
            Assert.Equal("15.5", target.toolchain.sdkVersion);
            Assert.Equal(new[] { cpp, c }, ToolchainResolver.ResolveHeaderSearchPaths(target, SourceLanguage.Cpp).Select(path => path.path));
            Assert.Equal(new[] { c }, ToolchainResolver.ResolveHeaderSearchPaths(target, SourceLanguage.C).Select(path => path.path));
            Assert.All(ToolchainResolver.ResolveHeaderSearchPaths(target, SourceLanguage.Cpp), path => Assert.False(path.isBuiltin));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SDKROOT", previousSdk);
            Environment.SetEnvironmentVariable("DEVELOPER_DIR", previousDeveloper);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExplicitSdkRootTakesPrecedenceOverTheDeveloperInstallation()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-apple-explicit-sdk-" + Guid.NewGuid().ToString("N"));
        string sdk = CreateSdk(root);
        string? previousSdk = Environment.GetEnvironmentVariable("SDKROOT"), previousDeveloper = Environment.GetEnvironmentVariable("DEVELOPER_DIR");
        try
        {
            Environment.SetEnvironmentVariable("SDKROOT", sdk);
            Environment.SetEnvironmentVariable("DEVELOPER_DIR", Path.Combine(root, "uninstalled-Xcode.app"));
            Assert.Equal(sdk, AppleSdkDiscovery.FindInstalledRoot("macosx"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SDKROOT", previousSdk);
            Environment.SetEnvironmentVariable("DEVELOPER_DIR", previousDeveloper);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateSdk(string parent)
    {
        string sdk = Path.GetFullPath(Path.Combine(parent, "MacOSX15.5.sdk"));
        string cpp = Path.Combine(sdk, "usr", "include", "c++", "v1");
        Directory.CreateDirectory(cpp);
        File.WriteAllText(Path.Combine(sdk, "SDKSettings.json"), """{"CanonicalName":"macosx15.5","Version":"15.5"}""");
        File.WriteAllText(Path.Combine(cpp, "cstddef"), "// Test-owned SDK header identity.\n");
        return sdk;
    }
}
