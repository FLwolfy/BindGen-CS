using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.Extension.AppleMobile;
using BGCS.Extension.BrowserWasm;
using BGCS.Targets.Desktop.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class NativeTargetProviderTests : FrontendTestHost
{
    [Fact]
    public async Task CustomProviderParsesThroughTheSamePublicFrontend()
    {
        NativeTargetDescriptor target = new TargetResolver([new FixtureTargetProvider()]).Resolve(new(new("fixture-riscv64-gnu")));
        NativeModule module = (await ParseSourceAsync("struct TargetValues { void* address; long count; };", target, SourceLanguage.C)).module;
        Assert.DoesNotContain(module.diagnostics, value => value.isFailure);
        Assert.Equal("riscv64-unknown-linux-gnu", module.triple);
        Assert.Equal(16, module.GetType(module.declarations.Single(value => value.name == "TargetValues").type!.Value).size);
    }
    [Fact]
    public void DuplicateProviderClaimsAreRejected() => Assert.Throws<InvalidOperationException>(() => new TargetResolver([new FixtureTargetProvider(), new FixtureTargetProvider()]).Resolve(new(new("fixture-riscv64-gnu"))));
    [Fact]
    public void EmptyCompositionHasNoBuiltInFallback() => Assert.Throws<ArgumentException>(() => new TargetResolver([]).Resolve(new(new("windows-x64-msvc"))));
    [Theory]
    [InlineData("ios-arm64-darwin", "arm64-apple-ios", "ios")]
    [InlineData("ios-simulator-arm64-darwin", "arm64-apple-ios-simulator", "ios-simulator")]
    [InlineData("ios-simulator-x64-darwin", "x86_64-apple-ios-simulator", "ios-simulator")]
    public void MobileDevicesAndSimulatorsRequireTheirExplicitProvider(string id, string triple, string platform)
    {
        Assert.False(new BGCS.Targets.Desktop.DesktopTargetProvider().TryResolve(new(new(id)), out _));
        Assert.True(new AppleMobileTargetProvider().TryResolve(new(new(id)), out NativeTargetDescriptor? target));
        Assert.Equal(triple, target!.triple); Assert.Equal(platform, target.platformId);
    }
    [Fact]
    public async Task TargetDefinitionsDoNotLeakAcrossFrontendRequests()
    {
        NativeTargetDescriptor custom = new TargetResolver([new FixtureTargetProvider()]).Resolve(new(new("fixture-riscv64-gnu")));
        Assert.True(new BrowserWasmTargetProvider().TryResolve(new(new("emscripten-wasm32-emscripten")), out NativeTargetDescriptor? browser));
        NativeModule first = (await ParseSourceAsync("#ifndef FIXTURE_FEATURE\n#error provider define missing\n#endif\n#ifndef CALLER_FEATURE\n#error caller missing\n#endif\nint fixture();", custom, defines: ["CALLER_FEATURE=1"])).module;
        NativeModule next = (await ParseSourceAsync("#ifdef FIXTURE_FEATURE\n#error stale provider define\n#endif\n#ifndef CALLER_FEATURE\n#error caller missing\n#endif\nint fixture();", browser, defines: ["CALLER_FEATURE=1"])).module;
        Assert.DoesNotContain(first.diagnostics.Concat(next.diagnostics), value => value.isFailure);
        Assert.Empty(browser!.toolchain.arguments);
    }
    [Fact]
    public void BrowserSdkHeadersStayInTheExplicitlySelectedSysroot()
    {
        string root = Path.GetFullPath("fixture-sdk");
        Assert.True(new BrowserWasmTargetProvider().TryResolve(new(new("emscripten-wasm32-emscripten"), new(sysRoot: root, systemIncludeFolders: ["caller-include"])), out NativeTargetDescriptor? target));
        Assert.Contains("caller-include", target!.toolchain.systemIncludeFolders);
        Assert.Contains(Path.Combine(root, "include"), target.toolchain.systemIncludeFolders);
        Assert.Equal(root, target.toolchain.sysRoot);
    }
    [Fact]
    public void ToolchainDescriptorCopiesCallerCollections()
    {
        string[] defines = ["ORIGINAL=1"]; NativeToolchainDescriptor descriptor = new(defines: defines); defines[0] = "CHANGED=1";
        Assert.Equal("ORIGINAL=1", Assert.Single(descriptor.defines)); Assert.False(descriptor.defines is string[]);
    }
    private sealed class FixtureTargetProvider : INativeTargetProvider
    {
        public bool TryResolve(NativeTargetRequest request, [NotNullWhen(true)] out NativeTargetDescriptor? target)
        {
            target = request.targetId.value == "fixture-riscv64-gnu" ? new(request.targetId, "fixture", "riscv64", "gnu", "riscv64-unknown-linux-gnu", new(defines: ["FIXTURE_FEATURE=1"], arguments: ["-fno-builtin"]), new(false, true), NativeByteOrder.LittleEndian) : null;
            return target is not null;
        }
    }
}
