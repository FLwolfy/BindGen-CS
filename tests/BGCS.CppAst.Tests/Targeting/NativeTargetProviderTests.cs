using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using BGCS.Core.Targeting;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.CppAst.Tests;

public sealed class NativeTargetProviderTests {
    [Fact]
    public void Resolve_CustomProvider_ParsesThroughTheSameClangEntry() {
        ClangTargetResolver resolver = new([new FixtureTargetProvider()]);
        NativeTargetDescriptor target = resolver.Resolve(new(new NativeTargetId("fixture-riscv64-gnu")));
        CppParserOptions options = new() {
            parserKind = CppParserKind.C,
            parseSystemIncludes = false,
            parseComments = false
        };
        options.ConfigureForTarget(target, discoverHostToolchain: false);

        CppCompilation compilation = CppParser.Parse(
            "struct TargetValues { void* address; long count; };",
            options);

        Assert.False(compilation.hasErrors, string.Join(Environment.NewLine, compilation.diagnostics.messages));
        Assert.Equal("riscv64-unknown-linux-gnu", options.targetTriple);
        Assert.Equal(16, Assert.Single(compilation.classes).sizeOf);
    }

    [Fact]
    public void Resolve_DuplicateClaims_RejectsAmbiguousComposition() {
        ClangTargetResolver resolver = new([new FixtureTargetProvider(), new FixtureTargetProvider()]);

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new(new NativeTargetId("fixture-riscv64-gnu"))));
    }

    [Fact]
    public void Resolve_EmptyComposition_DoesNotUseBuiltInFallback() {
        ClangTargetResolver resolver = new([]);

        Assert.Throws<ArgumentException>(() => resolver.Resolve(new(new NativeTargetId("windows-x64-msvc"))));
    }

    [Theory]
    [InlineData("ios-arm64-darwin", "arm64-apple-ios", "ios")]
    [InlineData("ios-simulator-arm64-darwin", "arm64-apple-ios-simulator", "ios-simulator")]
    [InlineData("ios-simulator-x64-darwin", "x86_64-apple-ios-simulator", "ios-simulator")]
    public void Resolve_AppleDeviceAndSimulator_KeepDistinctTargets(
        string targetId,
        string triple,
        string platform
    ) {
        NativeTargetDescriptor target = new ClangTargetResolver().Resolve(new(new NativeTargetId(targetId)));

        Assert.Equal(targetId, target.targetId.value);
        Assert.Equal(triple, target.triple);
        Assert.Equal(platform, target.platformId);
    }

    [Fact]
    public void ConfigureForTarget_ReplacesProviderDefinesAndPreservesCallerDefines() {
        ClangTargetResolver resolver = new([new FixtureTargetProvider()]);
        NativeTargetDescriptor custom = resolver.Resolve(new(new NativeTargetId("fixture-riscv64-gnu")));
        NativeTargetDescriptor wasm = new ClangTargetResolver().Resolve(new(new NativeTargetId("emscripten-wasm32-emscripten")));
        CppParserOptions options = new();
        options.defines.Add("CALLER_FEATURE=1");
        options.ConfigureForTarget(custom, discoverHostToolchain: false);
        options.ConfigureForTarget(wasm, discoverHostToolchain: false);

        Assert.Contains("CALLER_FEATURE=1", options.defines);
        Assert.DoesNotContain("FIXTURE_FEATURE=1", options.defines);
        Assert.DoesNotContain("-fno-builtin", options.additionalArguments);
        Assert.Equal("wasm32-unknown-emscripten", options.targetTriple);
    }

    [Fact]
    public void Resolve_EmscriptenSdkHeaders_BelongToTheSelectedToolchain() {
        string sysroot = Path.GetFullPath("fixture-sdk");
        NativeTargetDescriptor target = new ClangTargetResolver().Resolve(new(
            new NativeTargetId("emscripten-wasm32-emscripten"),
            new(sysRoot: sysroot, systemIncludeFolders: ["caller-include"])));
        CppParserOptions options = new() { parserKind = CppParserKind.C };
        options.ConfigureForTarget(target, discoverHostToolchain: false);

        Assert.Contains("caller-include", options.systemIncludeFolders);
        Assert.Contains(Path.Combine(sysroot, "include"), options.systemIncludeFolders);
        Assert.Contains("--sysroot=" + sysroot, options.additionalArguments);
        Assert.Contains("-isysroot", options.additionalArguments);
        Assert.Contains(sysroot, options.additionalArguments);
        options.ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId("windows-x64-msvc"))),
            discoverHostToolchain: false);
        Assert.DoesNotContain(Path.Combine(sysroot, "include"), options.systemIncludeFolders);
    }

    [Fact]
    public void ToolchainDescriptor_CopiesCallerCollections() {
        string[] defines = ["ORIGINAL=1"];
        NativeToolchainDescriptor descriptor = new(defines: defines);
        defines[0] = "CHANGED=1";

        Assert.Equal("ORIGINAL=1", Assert.Single(descriptor.defines));
        Assert.False(descriptor.defines is string[]);
    }

    private sealed class FixtureTargetProvider : INativeTargetProvider {
        public bool TryResolve(
            NativeTargetRequest request,
            [NotNullWhen(true)] out NativeTargetDescriptor? target
        ) {
            target = null;
            if (request.targetId.value != "fixture-riscv64-gnu")
                return false;
            target = new(
                request.targetId,
                "fixture",
                "riscv64",
                "gnu",
                request.tripleOverride ?? "riscv64-unknown-linux-gnu",
                new(defines: ["FIXTURE_FEATURE=1"], arguments: ["-fno-builtin"]));
            return true;
        }
    }
}
