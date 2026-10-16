using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class GnuDeclarationPreludeTests
{
    [Fact]
    public async Task RealGnuLeadingSdkViewPreservesUserBranchesCommentsMacrosAndUntrustedSystemHeaders()
    {
        NativeTargetDescriptor? target = Installed(); if (target is null) return;
        using Fixture fixture = new();
        string userSystem = Path.Combine(fixture.root, "user-system"); Directory.CreateDirectory(userSystem);
        await File.WriteAllTextAsync(Path.Combine(userSystem, "user-system.hpp"), "struct UserSystem { int value; };\n");
        await File.WriteAllTextAsync(fixture.header, """
            // Before the native SDK include, this is inert user documentation.
            #define USER_ABI 17
            #include <cstddef>
            #include <user-system.hpp>
            #if defined(__GNUC__) && !defined(__clang__)
            struct ActualGnu { int value; };
            #else
            struct WrongCompiler { int value; };
            #endif
            #define FINAL 42
            """);
        FrontendRequest request = fixture.Request(target) with { arguments = ["-isystem", userSystem] };
        NativeSourceViewResult result = await fixture.Prepare(request, "first");
        Assert.NotNull(result.declarationPrelude);
        Assert.NotEmpty(result.declarationPrelude.headers);
        Assert.All(result.declarationPrelude.headers, header => Assert.False(header.StartsWith(userSystem, StringComparison.Ordinal)));
        Assert.Contains("#define USER_ABI 17", result.declarationPrelude.source);
        Assert.Contains("struct ActualGnu", result.expandedSource);
        Assert.Contains("struct UserSystem", result.expandedSource);
        Assert.Contains("// Before the native SDK include", result.expandedSource);
        Assert.DoesNotContain("struct WrongCompiler", result.expandedSource);
        Assert.Equal("42", Assert.Single(result.macros, macro => macro.name == "FINAL").replacement);
        Assert.True(SourceSnapshotVerifier.Matches(result.inputs));
        NativeSourceViewResult replay = await fixture.Prepare(request, "second");
        Assert.Equal(result.observationFingerprint, replay.observationFingerprint);
    }

    [Theory]
    [InlineData("_Pragma")]
    [InlineData("__BASE_FILE__")]
    [InlineData("__COUNTER__")]
    [InlineData("__DATE__")]
    [InlineData("__FILE_NAME__")]
    [InlineData("__FILE__")]
    [InlineData("__FLT_EVAL_METHOD__")]
    [InlineData("__INCLUDE_LEVEL__")]
    [InlineData("__LINE__")]
    [InlineData("__MODULE__")]
    [InlineData("__TIMESTAMP__")]
    [InlineData("__TIME__")]
    [InlineData("__building_module")]
    [InlineData("__has_attribute")]
    [InlineData("__has_builtin")]
    [InlineData("__has_c_attribute")]
    [InlineData("__has_constexpr_builtin")]
    [InlineData("__has_cpp_attribute")]
    [InlineData("__has_declspec_attribute")]
    [InlineData("__has_embed")]
    [InlineData("__has_extension")]
    [InlineData("__has_feature")]
    [InlineData("__has_include")]
    [InlineData("__has_include_next")]
    [InlineData("__has_warning")]
    [InlineData("__identifier")]
    [InlineData("__is_identifier")]
    [InlineData("__is_target_arch")]
    [InlineData("__is_target_environment")]
    [InlineData("__is_target_os")]
    [InlineData("__is_target_variant_environment")]
    [InlineData("__is_target_variant_os")]
    [InlineData("__is_target_vendor")]
    [InlineData("__pragma")]
    [InlineData("__has_future_context")]
    public async Task RealGnuContextDependentReplacementBodiesCannotBeReevaluatedInsideThePrelude(string builtin)
    {
        NativeTargetDescriptor? target = Installed(); if (target is null) return;
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, "#define USER_STATE(x) " + builtin + "(x)\n#include <cstddef>\nint selected();\n");
        NotSupportedException failure = await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Prepare(fixture.Request(target), "state"));
        Assert.Contains("cannot prove", failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealGnuConsumedCounterInUserAncestorCannotChangeTheSdkEntryState(bool ancestor)
    {
        NativeTargetDescriptor? target = Installed(); if (target is null) return;
        using Fixture fixture = new();
        string include = "#include <cstddef>\n";
        if (ancestor)
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.root, "child.hpp"), include);
            include = "#include \"child.hpp\"\n";
        }
        await File.WriteAllTextAsync(fixture.header, "#if __COUNTER__ == 0\n#define USER_SELECTED 1\n#endif\n" + include + "int selected();\n");
        NotSupportedException failure = await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Prepare(fixture.Request(target), "counter"));
        Assert.Contains("counter", failure.Message);
    }

    [Theory]
    [InlineData("#define USER_STATE(x) #x\n")]
    [InlineData("#define USER_STATE(a, b) a ## b\n")]
    [InlineData("#define USER_STATE(a, b) a %:%: b\n")]
    public async Task RealGnuReplayCannotConstructHiddenCompilerContextThroughMacroOperators(string definition)
    {
        NativeTargetDescriptor? target = Installed(); if (target is null) return;
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, definition + "#include <cstddef>\nint selected();\n");
        NotSupportedException failure = await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Prepare(fixture.Request(target), "operator"));
        Assert.Contains("operator", failure.Message);
    }

    [Fact]
    public async Task RealGnuLiteralCounterAndMacroOperatorSpellingsRemainInert()
    {
        NativeTargetDescriptor? target = Installed(); if (target is null) return;
        using Fixture fixture = new();
        const string definition = "#define USER_DATA \"__COUNTER__ # ## %:%:\"\n";
        await File.WriteAllTextAsync(fixture.header, definition + "#include <cstddef>\nint selected();\n");
        NativeSourceViewResult result = await fixture.Prepare(fixture.Request(target), "inert");
        Assert.NotNull(result.declarationPrelude);
        Assert.Contains(definition.TrimEnd('\n'), result.declarationPrelude.source);
    }

    [Theory]
    [InlineData("#pragma pack(push, 1)\n")]
    [InlineData("#pragma push_macro(\"MODE\")\n")]
    [InlineData("#pragma pop_macro(\"MODE\")\n")]
    [InlineData("_Pragma(\"pack(push,1)\")\n")]
    public async Task RealGnuConsumedAncestorPragmasCannotDisappearAcrossUserHeaders(string prefix)
    {
        NativeTargetDescriptor? target = Installed(); if (target is null) return;
        using Fixture fixture = new();
        await File.WriteAllTextAsync(Path.Combine(fixture.root, "child.hpp"), "#include <cstddef>\n");
        await File.WriteAllTextAsync(fixture.header, prefix + "#include \"child.hpp\"\nint selected();\n");
        NotSupportedException failure = await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Prepare(fixture.Request(target), "ancestor"));
        Assert.Contains("pragma", failure.Message);
    }

    [Theory]
    [InlineData("__GNUC__")]
    [InlineData("__COUNTER__")]
    public async Task RealGnuUndefOfBuiltinIsNotLostWithItsInactiveDefinition(string name)
    {
        NativeTargetDescriptor? target = Installed(); if (target is null) return;
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, "#undef " + name + "\n#include <cstddef>\nint selected();\n");
        NotSupportedException failure = await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Prepare(fixture.Request(target), "undef"));
        Assert.Contains("builtin", failure.Message);
    }

    [Fact]
    public async Task RealGnuLateSdkIncludeDoesNotMoveAcrossFrozenUserDeclarations()
    {
        NativeTargetDescriptor? target = Installed(); if (target is null) return;
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, "struct Earlier { int value; };\n#include <cstddef>\n");
        NotSupportedException failure = await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Prepare(fixture.Request(target), "late"));
        Assert.Contains("follows user semantic tokens", failure.Message);
    }

    private static NativeTargetDescriptor? Installed()
    {
        if (OperatingSystem.IsWindows() || !OperatingSystem.IsLinux()
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BGCS_GNU_CXX")))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("BGCS_REQUIRE_GNU_SOURCE_VIEW"));
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("BGCS_REQUIRE_GNU"));
            return null;
        }
        NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new NativeTargetId("host"))), SourceLanguage.Cpp, "gnu");
        if (target.toolchain.compilerPath is null)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("BGCS_REQUIRE_GNU_SOURCE_VIEW"));
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("BGCS_REQUIRE_GNU"));
            return null;
        }
        return target;
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string root = Path.Combine(Path.GetTempPath(), "bgcs-real-gnu-prelude-" + Guid.NewGuid().ToString("N"));
        internal readonly string header;
        internal Fixture() { Directory.CreateDirectory(root); header = Path.Combine(root, "original.hpp"); }
        internal FrontendRequest Request(NativeTargetDescriptor target) => new(target, SourceLanguage.Cpp, [header])
            { pathBase = root, headerSearchPaths = ToolchainResolver.ResolveHeaderSearchPaths(target, SourceLanguage.Cpp).ToImmutableArray() };
        internal async Task<NativeSourceViewResult> Prepare(FrontendRequest source, string name)
            => await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, name)), CancellationToken.None);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
