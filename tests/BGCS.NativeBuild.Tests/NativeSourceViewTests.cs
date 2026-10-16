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

public sealed class NativeSourceViewTests
{
    [Theory]
    [InlineData("-Wp,-DBGCS_VIEW_CHANGE=1")]
    [InlineData("-Wa,--defsym,BGCS_VIEW_CHANGE=1")]
    [InlineData("-Wl,--defsym,BGCS_VIEW_CHANGE=1")]
    public async Task ForwardedDriverOptionsCannotBeDiscardedAsDiagnosticWarningFlags(string argument)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs source view forwarded option " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "input.h");
            await File.WriteAllTextAsync(header, "struct Value { int value; };\n");
            NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new NativeTargetId("windows-x64-msvc"))), SourceLanguage.C, "msvc");
            FrontendRequest source = new(target, SourceLanguage.C, [header]) { pathBase = root, arguments = [argument] };
            NotSupportedException failure = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "native"))));
            Assert.Contains("forwarded driver options", failure.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("response")]
    [InlineData("nested-response")]
    public async Task LegacyMsvcPreprocessorCannotBypassSourceViewCapabilityThroughResponseFiles(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs legacy preprocessor " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string input = Path.Combine(root, "input.c");
            await File.WriteAllTextAsync(input, "int native_value(void) { return 42; }");
            string argument = "/Zc:preprocessor-";
            if (mode != "direct")
            {
                await File.WriteAllTextAsync(Path.Combine(root, "inner.rsp"), argument);
                argument = "@inner.rsp";
                if (mode == "nested-response")
                {
                    await File.WriteAllTextAsync(Path.Combine(root, "outer.rsp"), argument);
                    argument = "@outer.rsp";
                }
            }
            NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new NativeTargetId("windows-x64-msvc"))), SourceLanguage.C, "msvc");
            var sdk = MsvcToolchainDiscovery.Resolve(target.toolchain.compilerPath!, "x64", target.toolchain.sysRoot, target.toolchain.sdkVersion);
            NativeBuildRequest request = new(target, [new(input, SourceLanguage.C, "c17")], Path.Combine(root, "scratch"), Path.Combine(root, "scratch", "unused.obj"))
            { argumentWorkingDirectory = root, outputKind = NativeOutputKind.Object, compilerArguments = [argument] };
            var failure = Assert.Throws<NotSupportedException>(() => new MsvcBuildProvider(sdk).Plan(request, default));
            Assert.Contains("conformant MSVC preprocessor", failure.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task MsvcViewUsesActualCompilerBranchesAndPreservesFinalMacroFacts(SourceLanguage language)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs native source view " + Guid.NewGuid().ToString("N"));
        string headers = Path.Combine(root, "headers"), scratch = Path.Combine(root, "native");
        Directory.CreateDirectory(headers);
        try
        {
            string header = Path.Combine(headers, "original.hpp");
            string ownedOutput = Path.Combine(headers, "Generated");
            Directory.CreateDirectory(ownedOutput);
            await File.WriteAllTextAsync(Path.Combine(ownedOutput, "candidate.txt"), "owned mutable output");
            string systemArguments = Path.Combine(root, "system-arguments");
            Directory.CreateDirectory(systemArguments);
            await File.WriteAllTextAsync(Path.Combine(systemArguments, "argument-system.h"), "#define FROM_SYSTEM_ARGUMENT 19\n");
            await File.WriteAllTextAsync(header, """
                #include <stdint.h>
                #include <argument-system.h>
                /** same documentation */
                #if defined(__clang__)
                struct UnexpectedClangBranch { float value; };
                #else
                struct ActualNativeBranch { int value; };
                #endif
                #define FULL64 18446744073709551615ULL
                #define NEGATIVE_ZERO (-0.0f)
                #define FUNCTION(x, ...) ((x) + __VA_ARGS__)
                #define VARIADIC_ONLY(...) __VA_ARGS__
                #define AMBIGUOUS 7
                #undef AMBIGUOUS
                #define AMBIGUOUS 7
                #define REMOVED 9
                #undef REMOVED
                /*
                #define FAKE_COMMENT 11
                */
                """ + (language == SourceLanguage.Cpp ? """

                    #define DIGITS 1'000
                    #define RAW R"marker(first
                    #define FAKE_RAW 13
                    last)marker"
                    #define RAW_SPLICED R"marker(first\
                    second)marker"
                    #define RAW_FALSE_CLOSE R"marker(first)mar\
                    ker"
                    #define FAKE_SPLICED_RAW 29
                    last)marker"
                    #define ORDINARY_SPLICED (2 + \
                    3)
                    """ : string.Empty));
            NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new NativeTargetId("windows-x64-msvc"))), language, "msvc");
            FrontendRequest source = new(target, language, [header])
            {
                pathBase = headers, defines = ["CONFIGURED=17"], arguments = ["-isystem", systemArguments], snapshotExcludedDirectories = [ownedOutput]
            };
            NativeSourceViewResult view = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, scratch));
            Assert.Contains("ActualNativeBranch", view.expandedSource);
            Assert.DoesNotContain("UnexpectedClangBranch", view.expandedSource);
            Assert.Contains("/** same documentation */", view.expandedSource);
            Assert.DoesNotContain("#define FULL64", view.expandedSource);
            NativeSourceMacro full = Assert.Single(view.macros, macro => macro.name == "FULL64");
            Assert.Equal("18446744073709551615ULL", full.replacement);
            Assert.Equal(header, full.location!.Value.file, ignoreCase: true);
            Assert.False(full.isSystem);
            NativeSourceMacro function = Assert.Single(view.macros, macro => macro.name == "FUNCTION");
            Assert.True(function.isFunctionLike);
            Assert.Equal(new[] { "x", "..." }, function.parameters);
            NativeSourceMacro variadic = Assert.Single(view.macros, macro => macro.name == "VARIADIC_ONLY");
            Assert.True(variadic.isFunctionLike);
            Assert.Equal(new[] { "..." }, variadic.parameters);
            Assert.Equal("__VA_ARGS__", variadic.replacement);
            Assert.Equal(header, variadic.location!.Value.file, ignoreCase: true);
            Assert.Null(Assert.Single(view.macros, macro => macro.name == "AMBIGUOUS").location);
            Assert.False(Assert.Single(view.macros, macro => macro.name == "CONFIGURED").isSystem);
            Assert.True(Assert.Single(view.macros, macro => macro.name == "_MSC_VER").isSystem);
            Assert.True(Assert.Single(view.macros, macro => macro.name == "FROM_SYSTEM_ARGUMENT").isSystem);
            Assert.DoesNotContain(view.macros, macro => macro.name is "REMOVED" or "FAKE_COMMENT" or "FAKE_RAW" or "FAKE_SPLICED_RAW" or "__clang__");
            if (language == SourceLanguage.Cpp)
            {
                Assert.Equal("1'000", Assert.Single(view.macros, macro => macro.name == "DIGITS").replacement);
                Assert.Contains("#define FAKE_RAW", Assert.Single(view.macros, macro => macro.name == "RAW").replacement);
                NativeSourceMacro spliced = Assert.Single(view.macros, macro => macro.name == "RAW_SPLICED");
                Assert.Contains("first\\\nsecond", spliced.replacement.Replace("\r\n", "\n", StringComparison.Ordinal));
                NativeSourceMacro falseClose = Assert.Single(view.macros, macro => macro.name == "RAW_FALSE_CLOSE");
                Assert.Contains("first)mar\\\nker\"", falseClose.replacement.Replace("\r\n", "\n", StringComparison.Ordinal));
                Assert.Contains("#define FAKE_SPLICED_RAW", falseClose.replacement);
                Assert.DoesNotContain('\\', Assert.Single(view.macros, macro => macro.name == "ORDINARY_SPLICED").replacement);
            }
            Assert.Contains(view.diagnostics, diagnostic => diagnostic.code == "BGCS-NATIVE-MACRO-ORIGIN");
            Assert.Contains(view.inputs.files, input => input.path.Equals(header, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(view.inputs.files, input => input.path.Equals(target.toolchain.compilerPath, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(view.inputs.files, input => Path.GetFileName(input.path).Equals(language == SourceLanguage.C ? "c1.dll" : "c1xx.dll", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(view.inputs.searchDirectories, input => input.path.Equals(Path.GetDirectoryName(target.toolchain.compilerPath), StringComparison.OrdinalIgnoreCase));
            Assert.Null(SourceSnapshotVerifier.GetMismatch(view.inputs));
            NativeSourceViewResult replay = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "native-replay")));
            Assert.Equal(view.observationFingerprint, replay.observationFingerprint);
            await File.WriteAllTextAsync(Path.Combine(ownedOutput, "another-candidate.txt"), "another owned output");
            Assert.Null(SourceSnapshotVerifier.GetMismatch(view.inputs));
            string appeared = Path.Combine(headers, "new-priority-header.h");
            await File.WriteAllTextAsync(appeared, "/* search membership changes */");
            Assert.False(SourceSnapshotVerifier.Matches(view.inputs));
            File.Delete(appeared);
            Assert.True(SourceSnapshotVerifier.Matches(view.inputs));
            await File.AppendAllTextAsync(header, "\n/* original source bytes change */\n");
            Assert.False(SourceSnapshotVerifier.Matches(view.inputs));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
