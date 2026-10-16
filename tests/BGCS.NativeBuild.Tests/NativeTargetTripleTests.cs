using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeTargetTripleTests
{
    [Theory]
    [InlineData("windows-x64-msvc", "x86_64-pc-windows-msvc")]
    [InlineData("windows-arm64-msvc", "aarch64-pc-windows-msvc")]
    [InlineData("linux-x64-gnu", "x86_64-pc-linux-gnu")]
    [InlineData("linux-arm64-musl", "aarch64-unknown-linux-musl")]
    [InlineData("android-arm64", "aarch64-linux-android26")]
    [InlineData("android-x64", "x86_64-unknown-linux-android35")]
    [InlineData("macos-x64-darwin", "x86_64-apple-macosx14.0")]
    [InlineData("macos-arm64-darwin", "arm64-apple-darwin24.2.0")]
    [InlineData("ios-arm64-darwin", "arm64-apple-ios18.0")]
    [InlineData("ios-simulator-arm64-darwin", "aarch64-apple-ios18.0-simulator")]
    [InlineData("freebsd-x64-gnu", "x86_64-unknown-freebsd14.2")]
    [InlineData("emscripten-wasm32-emscripten", "wasm32-unknown-emscripten")]
    public void ExplicitCompatibleTriplesRetainExactTargetAndDeploymentIdentity(string targetId, string triple)
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new(targetId), new(sysRoot: Path.GetTempPath()), triple));
        Assert.Equal(targetId, target.targetId.value);
        Assert.Equal(triple, target.triple);
    }

    [Theory]
    [InlineData("windows-x64-msvc", "aarch64-pc-windows-msvc")]
    [InlineData("windows-x64-msvc", "x86_64-pc-windows-gnu")]
    [InlineData("windows-x64-msvc", "x86_64-pc-windows-msvc19.30")]
    [InlineData("linux-x64-gnu", "x86_64-unknown-linux-musl")]
    [InlineData("linux-arm64-musl", "aarch64-unknown-linux-gnu")]
    [InlineData("linux-arm64-gnu", "aarch64_be-unknown-linux-gnu")]
    [InlineData("android-arm64", "aarch64-unknown-linux-gnu")]
    [InlineData("ios-arm64-darwin", "arm64-apple-ios18.0-simulator")]
    [InlineData("ios-simulator-arm64-darwin", "arm64-apple-ios18.0")]
    [InlineData("macos-arm64-darwin", "arm64-apple-ios18.0")]
    [InlineData("freebsd-x64-gnu", "x86_64-unknown-linux-gnu")]
    [InlineData("emscripten-wasm32-emscripten", "wasm32-unknown-wasi")]
    public void AnOverrideCannotReplaceTheDeclaredCpuPlatformOrAbi(string targetId, string triple)
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(() => new TargetResolver().Resolve(new(new(targetId), new(sysRoot: Path.GetTempPath()), triple)));
        Assert.Contains("incompatible", failure.Message);
        Assert.Contains(targetId, failure.Message);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("x86_64-pc-linux-gnu -m32")]
    [InlineData("x86_64-pc-linux-gnu\n")]
    public void TripleTokensCannotContainUnstructuredDriverOptions(string triple)
        => Assert.Throws<ArgumentException>(() => new NativeTargetRequest(new("linux-x64-gnu"), tripleOverride: triple));

    [Fact]
    public void CustomProvidersOwnTheirOwnExplicitTriplePolicy()
    {
        const string triple = "riscv64-acme-privateos-abi";
        NativeTargetDescriptor target = new TargetResolver([new CustomTarget()]).Resolve(new(new("vendor-riscv64"), tripleOverride: triple));
        Assert.Equal(triple, target.triple);
    }

    private sealed class CustomTarget : INativeTargetProvider
    {
        public bool TryResolve(NativeTargetRequest request, [NotNullWhen(true)] out NativeTargetDescriptor? target)
        {
            target = new(request.targetId, "privateos", "riscv64", "vendor", request.tripleOverride!, request.toolchain);
            return true;
        }
    }
}
