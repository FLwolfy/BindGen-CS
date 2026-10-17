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
using BGCS.Infrastructure.Workers;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class GnuDeclarationPreludeTests
{
    [Fact]
    public async Task RealGnuDebugWorkingDirectoryMetadataPreservesAdaptedFactsAndReplayIdentity()
    {
        NativeTargetDescriptor? target = Installed(); if (target is null) return;
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, "#include <cstddef>\nstruct DebugRecord { int value; };\n");
        FrontendRequest request = fixture.Request(target) with { arguments = ["-g"] };
        NativeSourceViewResult view = await fixture.Prepare(request, "debug-first");
        Assert.NotNull(view.declarationPrelude);
        Assert.NotNull(view.compilation);
        string raw = await File.ReadAllTextAsync(view.compilation.operation.outputFile);
        Assert.Contains(raw.Split('\n'), line => line.StartsWith("# 1 \"", StringComparison.Ordinal) && line.TrimEnd('\r').EndsWith("//\"", StringComparison.Ordinal));
        string frozen = Path.Combine(fixture.root, "debug-first", "frozen.hpp");
        await File.WriteAllTextAsync(frozen, view.expandedSource);
        FrontendResult parsed = await new FrontendWorkerClient(WorkerPath(), TimeSpan.FromSeconds(90)).ParseAsync(request with
        {
            headers = [frozen], declarationPrelude = view.declarationPrelude, arguments = [],
            parseMacros = false, snapshotExcludedDirectories = [Path.Combine(fixture.root, "debug-first")]
        }, CancellationToken.None);
        Assert.DoesNotContain(parsed.module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.Contains(parsed.module.declarations, declaration => declaration.qualifiedName == "DebugRecord" && declaration.isCompleteDefinition);
        NativeSourceViewResult replay = await fixture.Prepare(request, "debug-second");
        Assert.Equal(view.observationFingerprint, replay.observationFingerprint);
        Assert.Equal(raw, await File.ReadAllTextAsync(view.compilation.operation.outputFile));
        Assert.True(SourceSnapshotVerifier.Matches(view.inputs));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealGnuMultipleSdkReturnsAndNestedUserEntriesRemainParseableInThePublicWorker(bool systemUserHeaders)
    {
        NativeTargetDescriptor? target = Installed(); if (target is null) return;
        using Fixture fixture = new();
        string users = Path.Combine(fixture.root, "user-includes"); Directory.CreateDirectory(users);
        await File.WriteAllTextAsync(Path.Combine(users, "first.hpp"), "// first user include\n#include <cstddef>\n#define FIRST_USER 17\n");
        await File.WriteAllTextAsync(Path.Combine(users, "second.hpp"), "// second user include\n#include <type_traits>\n#define SECOND_USER 25\n");
        await File.WriteAllTextAsync(fixture.header, """
            #include <first.hpp>
            #include <second.hpp>
            struct FrozenRecord { int value; };
            static_assert(sizeof(FrozenRecord) == sizeof(int));
            int native_consume(FrozenRecord value);
            """);
        FrontendRequest request = fixture.Request(target) with
        { arguments = [systemUserHeaders ? "-isystem" : "-I", users] };
        NativeSourceViewResult view = await fixture.Prepare(request, "multiple");
        Assert.NotNull(view.declarationPrelude);
        Assert.True(view.declarationPrelude.headers.Length >= 2);
        string frozen = Path.Combine(fixture.root, "multiple", "frozen.hpp");
        await File.WriteAllTextAsync(frozen, view.expandedSource);
        FrontendResult parsed = await new FrontendWorkerClient(WorkerPath(), TimeSpan.FromSeconds(90)).ParseAsync(request with
        {
            headers = [frozen], declarationPrelude = view.declarationPrelude,
            parseMacros = false, usePresumedSourceLocations = true,
            snapshotExcludedDirectories = [Path.Combine(fixture.root, "multiple")]
        }, CancellationToken.None);
        Assert.DoesNotContain(parsed.module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.True(parsed.requiresNativeCompilerProof);
        var record = Assert.Single(parsed.module.declarations, declaration => declaration.kind == "Class" && declaration.name == "FrozenRecord");
        Assert.Equal(4, parsed.module.GetType(record.type!.Value).size);
        Assert.Equal(0, Assert.Single(record.children.Select(parsed.module.GetDeclaration), declaration => declaration.kind == "Field").offset);
        Assert.Contains(parsed.module.declarations, declaration => declaration.kind == "Function" && declaration.name == "native_consume");
        Assert.True(SourceSnapshotVerifier.Matches(parsed.snapshot));
        Assert.True(SourceSnapshotVerifier.Matches(view.inputs));
    }

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

    private static string WorkerPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "BGCS.Frontend.Clang.Worker")))
            directory = directory.Parent;
        string root = directory?.FullName ?? throw new InvalidOperationException("The test repository is unavailable.");
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? "Release" : "Debug";
        return Path.Combine(root, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0", "BGCS.Frontend.Clang.Worker.dll");
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
