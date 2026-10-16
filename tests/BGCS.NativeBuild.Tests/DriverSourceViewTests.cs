using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
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

public sealed class DriverSourceViewTests
{
    [Theory]
    [InlineData("clang", SourceLanguage.C)]
    [InlineData("clang", SourceLanguage.Cpp)]
    [InlineData("gnu", SourceLanguage.C)]
    [InlineData("gnu", SourceLanguage.Cpp)]
    public async Task SelectedDriverControlsConditionalDeclarationsAndFinalMacrosWithStableReplay(string family, SourceLanguage language)
    {
        NativeTargetDescriptor? target = Installed(family, language);
        if (target is null) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs driver source view " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "original.h"), system = Path.Combine(root, "system");
            Directory.CreateDirectory(system);
            await File.WriteAllTextAsync(Path.Combine(system, "argument-system.h"), "#define FROM_ARGUMENT_SYSTEM 19\n");
            await File.WriteAllTextAsync(header, """
                #include <stdint.h>
                #include <argument-system.h>
                /** actual selected driver documentation */
                #if defined(__clang__)
                struct ActualClangBranch { int value; };
                #else
                struct ActualGnuBranch { int value; };
                #endif
                #define FULL64 18446744073709551615ULL
                #define NEGATIVE_ZERO (-0.0f)
                #define FUNCTION(x, ...) ((x) + __VA_ARGS__)
                #define VARIADIC_ONLY(...) __VA_ARGS__
                #define FINAL 1
                #undef FINAL
                #define FINAL 42
                #define REMOVED 9
                #undef REMOVED
                /*
                #define FAKE_COMMENT 11
                */
                """);
            FrontendRequest source = new(target, language, [header])
            {
                pathBase = root, defines = ["CONFIGURED=17"], arguments = ["-isystem", system],
                headerSearchPaths = ToolchainResolver.ResolveHeaderSearchPaths(target, language).ToImmutableArray()
            };
            Assert.True(NativeSourceViewPreprocessor.IsSupported(source));
            NativeSourceViewResult view = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "native")));
            Assert.Equal(family, view.compilerFamily);
            Assert.NotEmpty(view.compilerIdentity);
            bool clang = view.macros.Any(macro => macro.name == "__clang__");
            Assert.Contains(clang ? "ActualClangBranch" : "ActualGnuBranch", view.expandedSource);
            Assert.DoesNotContain(clang ? "ActualGnuBranch" : "ActualClangBranch", view.expandedSource);
            Assert.Contains("/** actual selected driver documentation */", view.expandedSource);
            Assert.Equal("42", Assert.Single(view.macros, macro => macro.name == "FINAL").replacement);
            Assert.Equal("18446744073709551615ULL", Assert.Single(view.macros, macro => macro.name == "FULL64").replacement);
            Assert.Equal(new[] { "x", "..." }, Assert.Single(view.macros, macro => macro.name == "FUNCTION").parameters);
            Assert.Equal(new[] { "..." }, Assert.Single(view.macros, macro => macro.name == "VARIADIC_ONLY").parameters);
            Assert.True(Assert.Single(view.macros, macro => macro.name == "FROM_ARGUMENT_SYSTEM").isSystem);
            Assert.False(Assert.Single(view.macros, macro => macro.name == "CONFIGURED").isSystem);
            Assert.False(Assert.Single(view.macros, macro => macro.name == "FULL64").isSystem);
            Assert.DoesNotContain(view.macros, macro => macro.name is "REMOVED" or "FAKE_COMMENT");
            Assert.DoesNotContain("#define FULL64", view.expandedSource);
            Assert.Contains(view.inputs.files, input => input.path == header);
            Assert.Contains(view.inputs.files, input => input.path == target.toolchain.compilerPath);
            Assert.Null(SourceSnapshotVerifier.GetMismatch(view.inputs));
            NativeSourceViewResult replay = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "native-replay")));
            Assert.Equal(view.observationFingerprint, replay.observationFingerprint);
            await File.AppendAllTextAsync(header, "\n/* changed source bytes */\n");
            Assert.False(SourceSnapshotVerifier.Matches(view.inputs));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("response")]
    public async Task HiddenOutputControlCannotRemoveOriginalSourceProvenance(string mode)
    {
        NativeTargetDescriptor? target = Installed("clang", SourceLanguage.C);
        if (target is null) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs driver output control " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "input.h");
            await File.WriteAllTextAsync(header, "int original(int value);\n");
            await File.WriteAllTextAsync(Path.Combine(root, "control.rsp"), "-P");
            FrontendRequest source = new(target, SourceLanguage.C, [header]) { pathBase = root, arguments = [mode == "direct" ? "-P" : "@control.rsp"] };
            NotSupportedException failure = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "native"))));
            Assert.Contains("controlled preprocessing", failure.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SelfContainedCrossTargetPreprocessingDoesNotClaimNativeConstructionPrerequisites()
    {
        NativeTargetDescriptor? host = Installed("clang", SourceLanguage.C);
        if (host is null) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs source view without link sdk " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "input.h");
            await File.WriteAllTextAsync(header, "struct PointerRecord { void* value; };\n");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("emscripten-wasm32-emscripten"),
                new NativeToolchainDescriptor(compilerPath: host.toolchain.compilerPath)));
            FrontendRequest source = new(target, SourceLanguage.C, [header]) { pathBase = root };
            NativeSourceViewResult view = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "native")));
            Assert.Contains("struct PointerRecord", view.expandedSource);
            Assert.Equal("4", Assert.Single(view.macros, macro => macro.name == "__SIZEOF_POINTER__").replacement);
            NativeBuildRequest build = new(target, [new(header, SourceLanguage.C, "c17")], Path.Combine(root, "construction"), Path.Combine(root, "construction", "output.o"))
            { outputKind = NativeOutputKind.Object, argumentWorkingDirectory = root };
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new CompilerBuildProvider("clang", host.toolchain.compilerPath!).Plan(build, default));
            Assert.Contains("explicit verified SDK sysroot", failure.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CompilerExpandedBaseFileRemainsSemanticInputInsteadOfNormalizedScratchMetadata()
    {
        NativeTargetDescriptor? target = Installed("clang", SourceLanguage.C);
        if (target is null) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs native base file observation " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "input.h");
            await File.WriteAllTextAsync(header, "static const char* const primary = __BASE_FILE__;\n");
            FrontendRequest source = new(target, SourceLanguage.C, [header]) { pathBase = root };
            NativeSourceViewResult first = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "first")));
            NativeSourceViewResult second = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "second")));
            Assert.NotEqual(first.observationFingerprint, second.observationFingerprint);
            Assert.Contains("first/source-view.input.c", first.expandedSource.Replace("\\\\", "/", StringComparison.Ordinal));
            Assert.Contains("second/source-view.input.c", second.expandedSource.Replace("\\\\", "/", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LiteralContainingOwnedInputPathAndDirectiveLookingRawTextIsNeverRewritten()
    {
        NativeTargetDescriptor? target = Installed("clang", SourceLanguage.Cpp);
        if (target is null) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs native literal observation " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "input.hpp");
            string literal = Path.Combine(root, "first", "source-view.input.cpp").Replace('\\', '/');
            await File.WriteAllTextAsync(header, $$""""
                static const char* const path = "{{literal}}";
                static const char* const raw = R"delimiter(
                # 12 "{{literal}}" 3
                #line 13 "{{literal}}"
                )delimiter";
                """");
            FrontendRequest source = new(target, SourceLanguage.Cpp, [header]) { pathBase = root };
            NativeSourceViewResult first = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "first")));
            NativeSourceViewResult second = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "second")));
            Assert.Equal(first.observationFingerprint, second.observationFingerprint);
            Assert.Contains("# 12 \"" + literal + "\" 3", first.expandedSource);
            Assert.Contains("#line 13 \"" + literal + "\"", first.expandedSource);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ExplicitOwnedHeaderMappingsNormalizeMetadataAndMacroOriginsWithoutHidingLiteralPaths()
    {
        NativeTargetDescriptor? target = Installed("clang", SourceLanguage.C);
        if (target is null) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs mapped source observation " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string first = Path.Combine(root, "candidate-a", "bridge.h"), second = Path.Combine(root, "candidate-b", "bridge.h");
            Directory.CreateDirectory(Path.GetDirectoryName(first)!); Directory.CreateDirectory(Path.GetDirectoryName(second)!);
            string header = "#define MAPPED_VALUE 42\nint mapped(int value);\n";
            await File.WriteAllTextAsync(first, header); await File.WriteAllTextAsync(second, header);
            const string logical = "windows-x64-msvc/native/include/bridge.h";
            FrontendRequest a = new(target, SourceLanguage.C, [first]) { pathBase = root,
                sourcePathMappings = ImmutableDictionary<string, string>.Empty.Add(first, logical) };
            FrontendRequest b = a with { headers = [second], sourcePathMappings = ImmutableDictionary<string, string>.Empty.Add(second, logical) };
            NativeSourceViewResult left = await new NativeSourceViewPreprocessor().PrepareAsync(new(a, Path.Combine(root, "view-a")));
            NativeSourceViewResult right = await new NativeSourceViewPreprocessor().PrepareAsync(new(b, Path.Combine(root, "view-b")));
            Assert.Equal(left.observationFingerprint, right.observationFingerprint);
            Assert.Equal(first, Assert.Single(left.macros, macro => macro.name == "MAPPED_VALUE").location!.Value.file);
            Assert.Equal(second, Assert.Single(right.macros, macro => macro.name == "MAPPED_VALUE").location!.Value.file);
            Assert.Contains(left.inputs.files, file => file.path == first);
            Assert.Contains(right.inputs.files, file => file.path == second);
            await File.AppendAllTextAsync(first, "static const char* const actual_path = __FILE__;\n");
            await File.AppendAllTextAsync(second, "static const char* const actual_path = __FILE__;\n");
            NativeSourceViewResult literalA = await new NativeSourceViewPreprocessor().PrepareAsync(new(a, Path.Combine(root, "literal-a")));
            NativeSourceViewResult literalB = await new NativeSourceViewPreprocessor().PrepareAsync(new(b, Path.Combine(root, "literal-b")));
            Assert.NotEqual(literalA.observationFingerprint, literalB.observationFingerprint);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static NativeTargetDescriptor? Installed(string family, SourceLanguage language)
    {
        if (family == "gnu" && (OperatingSystem.IsWindows() || !OperatingSystem.IsLinux()
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(language == SourceLanguage.Cpp ? "BGCS_GNU_CXX" : "BGCS_GNU_CC"))))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("BGCS_REQUIRE_GNU_SOURCE_VIEW"));
            return null;
        }
        NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new NativeTargetId("host"))), language, family);
        if (target.toolchain.compilerPath is null)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable(family == "gnu" ? "BGCS_REQUIRE_GNU_SOURCE_VIEW" : "BGCS_REQUIRE_CLANG_SOURCE_VIEW"));
            return null;
        }
        return target;
    }
}
