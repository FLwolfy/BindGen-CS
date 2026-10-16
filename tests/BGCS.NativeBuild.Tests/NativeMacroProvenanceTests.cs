using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeMacroProvenanceTests
{
    [Theory]
    [InlineData("msvc", true)]
    [InlineData("msvc", false)]
    [InlineData("clang", true)]
    [InlineData("clang", false)]
    [InlineData("gnu", true)]
    [InlineData("gnu", false)]
    public async Task ExactCommandLineOriginsPreserveWarningsAndRejectSourceRedefinition(string family, bool parseMacros)
    {
        NativeTargetDescriptor? target = Installed(family);
        if (target is null) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs macro origins " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "original.hpp"), indirect = Path.Combine(root, "indirect.h");
            await File.WriteAllTextAsync(indirect, "#undef INDIRECT\n#define INDIRECT 11\n");
            string contents = """
                #warning BGCS_ORIGINAL_SOURCE_NOTICE
                #include "indirect.h"
                #undef REDEFINED
                #define REDEFINED 7
                #undef SAME_VALUE
                #define SAME_VALUE 9
                #if 0
                #undef INACTIVE
                #endif
                #define AMBIGUOUS 3
                #undef AMBIGUOUS
                #define AMBIGUOUS 3
                /* #undef DECLARED */
                static const char* const raw = R"literal(
                #undef DECLARED
                #define ARGUMENT 55
                )literal";
                int native_value(int input);
                """;
            // This MSVC release rejects #warning rather than issuing a diagnostic
            // warning. An actual unknown pragma exercises its supported C4068 path.
            if (family == "msvc") contents = contents.Replace("#warning BGCS_ORIGINAL_SOURCE_NOTICE", "#pragma BGCS_ORIGINAL_SOURCE_NOTICE", StringComparison.Ordinal);
            await File.WriteAllTextAsync(header, contents);
            FrontendRequest source = new(target, SourceLanguage.Cpp, [header])
            {
                pathBase = root,
                parseMacros = parseMacros,
                defines = ["DECLARED=17", "REDEFINED=1", "SAME_VALUE=9", "INDIRECT=11", "INACTIVE=13", "PREAMBLE=15", "POSTAMBLE=19"],
                arguments = ["-D", "ARGUMENT=23", "-DDEFAULT", "-DEMPTY=", "-DHISTORY=1", "-UHISTORY"],
                preamble = "#undef PREAMBLE\n#define PREAMBLE 15\n",
                postamble = "#undef POSTAMBLE\n#define POSTAMBLE 19\n"
            };
            if (family != "msvc") source = source with { arguments = source.arguments.AddRange(ImmutableArray.Create("-DORDERED=1", "-UORDERED", "-DORDERED=2")) };
            NativeSourceViewResult view = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "native")));
            foreach (var (name, value) in new[] { ("DECLARED", "17"), ("ARGUMENT", "23"), ("DEFAULT", "1"), ("EMPTY", "") }
                .Concat(family == "msvc" ? Array.Empty<(string, string)>() : new[] { ("ORDERED", "2") }))
            {
                NativeSourceMacro macro = Assert.Single(view.macros, macro => macro.name == name);
                Assert.Equal(value, macro.replacement);
                Assert.Null(macro.location);
                Assert.False(macro.isSystem);
                BindingDiagnostic origin = Assert.Single(view.diagnostics, diagnostic => diagnostic.code == "BGCS-NATIVE-MACRO-ORIGIN"
                    && diagnostic.message.StartsWith("Native macro '" + name + "'", StringComparison.Ordinal));
                Assert.Equal(BindingDiagnosticSeverity.Information, origin.severity);
                Assert.Null(origin.location);
                Assert.Contains("declared command-line definition", origin.message);
                Assert.Contains("provenance sha256:", origin.message);
            }
            foreach (string name in new[] { "REDEFINED", "SAME_VALUE", "INDIRECT", "INACTIVE", "PREAMBLE", "POSTAMBLE", "AMBIGUOUS" })
            {
                Assert.Null(Assert.Single(view.macros, macro => macro.name == name).location);
                BindingDiagnostic origin = Assert.Single(view.diagnostics, diagnostic => diagnostic.code == "BGCS-NATIVE-MACRO-ORIGIN"
                    && diagnostic.message.StartsWith("Native macro '" + name + "'", StringComparison.Ordinal));
                Assert.Equal(BindingDiagnosticSeverity.Warning, origin.severity);
                Assert.Contains("unknown source origin", origin.message);
            }
            BindingDiagnostic warning = Assert.Single(view.diagnostics, diagnostic => diagnostic.code == "BGCS-FRONTEND-NATIVE"
                && diagnostic.message.Contains("BGCS_ORIGINAL_SOURCE_NOTICE", StringComparison.Ordinal));
            Assert.Equal(BindingDiagnosticSeverity.Warning, warning.severity);
            Assert.Equal(header, warning.location!.Value.file, ignoreCase: OperatingSystem.IsWindows());
            Assert.Equal(1, warning.location.Value.line);
            if (family == "msvc") Assert.Contains(view.diagnostics, diagnostic => diagnostic.code == "BGCS-FRONTEND-NATIVE"
                && diagnostic.severity == BindingDiagnosticSeverity.Warning && diagnostic.location is null
                && diagnostic.message.StartsWith("D9025:", StringComparison.Ordinal));
            NativeSourceViewResult replay = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "replay")));
            Assert.Equal(view.observationFingerprint, replay.observationFingerprint);
            Assert.Equal(view.diagnostics.ToArray(), replay.diagnostics.ToArray());
            // Final native tokens stay equal, but a different declared argv history
            // cannot reuse an origin receipt belonging to the original arguments.
            NativeSourceViewResult changed = await new NativeSourceViewPreprocessor().PrepareAsync(new(source with
            { arguments = source.arguments.Replace("-DHISTORY=1", "-DHISTORY=6") }, Path.Combine(root, "changed")));
            Assert.DoesNotContain(view.macros, macro => macro.name == "HISTORY");
            Assert.DoesNotContain(changed.macros, macro => macro.name == "HISTORY");
            Assert.NotEqual(view.observationFingerprint, changed.observationFingerprint);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("clang")]
    [InlineData("gnu")]
    public async Task FunctionMacroCommandLineShapeAndReplacementRemainExact(string family)
    {
        NativeTargetDescriptor? target = Installed(family);
        if (target is null) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs function macro origins " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "original.h");
            await File.WriteAllTextAsync(header, "int native_value(int input);\n");
            FrontendRequest source = new(target, SourceLanguage.C, [header])
            { pathBase = root, arguments = ["-DFUNCTION(x,...)=((x) + __VA_ARGS__)"] };
            NativeSourceViewResult view = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "native")));
            NativeSourceMacro macro = Assert.Single(view.macros, macro => macro.name == "FUNCTION");
            Assert.True(macro.isFunctionLike);
            Assert.Equal(new[] { "x", "..." }, macro.parameters);
            Assert.Null(macro.location);
            Assert.Equal(BindingDiagnosticSeverity.Information, Assert.Single(view.diagnostics,
                diagnostic => diagnostic.code == "BGCS-NATIVE-MACRO-ORIGIN" && diagnostic.message.StartsWith("Native macro 'FUNCTION'", StringComparison.Ordinal)).severity);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DriverSearchOperandCannotBeMistakenForADeclaredMacro()
    {
        NativeTargetDescriptor? target = Installed("clang");
        if (target is null) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs macro option operand " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string operand = "-D__SIZEOF_POINTER__=" + IntPtr.Size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Directory.CreateDirectory(Path.Combine(root, operand));
            string header = Path.Combine(root, "original.h");
            await File.WriteAllTextAsync(header, "int native_value(int input);\n");
            FrontendRequest source = new(target, SourceLanguage.C, [header]) { pathBase = root, arguments = ["-I", operand] };
            NativeSourceViewResult view = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "native")));
            Assert.Contains("native_value", view.expandedSource);
            Assert.True(Assert.Single(view.macros, macro => macro.name == "__SIZEOF_POINTER__").isSystem);
            Assert.DoesNotContain(view.diagnostics, diagnostic => diagnostic.code == "BGCS-NATIVE-MACRO-ORIGIN"
                && diagnostic.message.StartsWith("Native macro '__SIZEOF_POINTER__'", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static NativeTargetDescriptor? Installed(string family)
    {
        if (family == "msvc" && !OperatingSystem.IsWindows()) return null;
        if (family == "gnu" && (OperatingSystem.IsWindows() || !OperatingSystem.IsLinux()
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BGCS_GNU_CXX"))))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("BGCS_REQUIRE_GNU_SOURCE_VIEW"));
            return null;
        }
        NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new NativeTargetId("host"))), SourceLanguage.Cpp, family);
        if (target.toolchain.compilerPath is null)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable(family == "gnu" ? "BGCS_REQUIRE_GNU_SOURCE_VIEW" : "BGCS_REQUIRE_CLANG_SOURCE_VIEW"));
            return null;
        }
        return target;
    }
}
