using System;
using System.IO;
using System.Linq;
using BGCS.Core.Inputs;
using BGCS.Core.Targets;
using BGCS.Targets.Desktop.Targeting;
using BGCS.Targets.Desktop.Toolchains;
using Xunit;

namespace BGCS.Targets.Desktop.Tests;

[CollectionDefinition("Apple SDK environment", DisableParallelization = true)]
public sealed class AppleSdkEnvironmentCollection;

[Collection("Apple SDK environment")]
public sealed class AppleSdkDiscoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitSdkResolutionWinsOverMismatchedOrMissingAmbientSdkRoot(bool missing)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-apple-explicit-priority-" + Guid.NewGuid().ToString("N"));
        string sdk = CreateSdk(root), ambient = Path.Combine(root, "iPhoneOS18.sdk");
        if (!missing)
        {
            Directory.CreateDirectory(ambient);
            File.WriteAllText(Path.Combine(ambient, "SDKSettings.json"), """{"CanonicalName":"iphoneos18.0","Version":"18.0"}""");
        }
        string? previousSdk = Environment.GetEnvironmentVariable("SDKROOT"), previousDeveloper = Environment.GetEnvironmentVariable("DEVELOPER_DIR");
        try
        {
            Environment.SetEnvironmentVariable("SDKROOT", ambient);
            Environment.SetEnvironmentVariable("DEVELOPER_DIR", Path.Combine(root, "uninstalled-developer"));
            if (missing) Assert.Throws<DirectoryNotFoundException>(() => AppleSdkDiscovery.FindInstalledRoot("macosx"));
            else Assert.Throws<InvalidDataException>(() => AppleSdkDiscovery.FindInstalledRoot("macosx"));
            NativeTargetDescriptor target = AppleSdkDiscovery.Resolve(new("macos-x64-darwin"), sdk);
            Assert.Equal(sdk, target.toolchain.sysRoot);
            Assert.Equal("15.5", target.toolchain.sdkVersion);
            Assert.Equal(new[] { Path.Combine(sdk, "usr", "include") }, target.toolchain.systemIncludeFolders);
            Assert.Equal(new[] { Path.Combine(sdk, "usr", "include", "c++", "v1") }, target.toolchain.cxxSystemIncludeFolders);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SDKROOT", previousSdk);
            Environment.SetEnvironmentVariable("DEVELOPER_DIR", previousDeveloper);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("iphoneos", "ios-arm64-darwin")]
    [InlineData("iphonesimulator", "ios-simulator-arm64-darwin")]
    public void MacSdkRootCannotBeSelectedForDeviceOrSimulator(string family, string targetId)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-apple-sdk-family-" + Guid.NewGuid().ToString("N"));
        string sdk = CreateSdk(root);
        string? previousSdk = Environment.GetEnvironmentVariable("SDKROOT"), previousDeveloper = Environment.GetEnvironmentVariable("DEVELOPER_DIR");
        try
        {
            Environment.SetEnvironmentVariable("SDKROOT", sdk);
            Environment.SetEnvironmentVariable("DEVELOPER_DIR", Path.Combine(root, "other-developer"));
            Assert.Throws<InvalidDataException>(() => BGCS.Extension.AppleMobile.AppleSdkDiscovery.FindInstalledRoot(family));
            Assert.Equal(sdk, AppleSdkDiscovery.FindInstalledRoot("macosx"));
            if (OperatingSystem.IsMacOS())
                Assert.Throws<InvalidDataException>(() => new BGCS.Extension.AppleMobile.AppleMobileTargetProvider().TryResolve(new(new(targetId)), out _));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SDKROOT", previousSdk);
            Environment.SetEnvironmentVariable("DEVELOPER_DIR", previousDeveloper);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SelectedAppleSdkFrameworkRootsRemainExplicitAndNeverUseHostFrameworks()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-apple-framework-roots-" + Guid.NewGuid().ToString("N"));
        string sdk = CreateSdk(root);
        string framework = Path.Combine(sdk, "System", "Library", "Frameworks");
        Directory.CreateDirectory(framework);
        try
        {
            NativeTargetDescriptor target = AppleSdkDiscovery.Resolve(new("macos-x64-darwin"), sdk);
            var paths = ToolchainResolver.ResolveHeaderSearchPaths(target, SourceLanguage.C);
            Assert.Equal(new[] { Path.Combine(sdk, "usr", "include"), framework }, paths.Select(path => path.path));
            Assert.False(paths[0].isFramework);
            Assert.True(paths[1].isFramework);
            Assert.All(paths, path => Assert.StartsWith(sdk + Path.DirectorySeparatorChar, path.path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

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
