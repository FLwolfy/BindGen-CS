using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeStringLiteralConsumerTests
{
    [Theory]
    [InlineData(SourceLanguage.C, BindingImportMode.FunctionTable)]
    [InlineData(SourceLanguage.C, BindingImportMode.DllImport)]
    [InlineData(SourceLanguage.C, BindingImportMode.LibraryImport)]
    [InlineData(SourceLanguage.Cpp, BindingImportMode.FunctionTable)]
    [InlineData(SourceLanguage.Cpp, BindingImportMode.DllImport)]
    [InlineData(SourceLanguage.Cpp, BindingImportMode.LibraryImport)]
    public async Task GeneratedLiteralValuesMatchEveryActualNativeUnitIncludingEmbeddedAndFinalZero(SourceLanguage language, BindingImportMode importMode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-literal-consumer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "literal.h"), Header);
            string source = "literal." + (language == SourceLanguage.C ? "c" : "cpp");
            await File.WriteAllTextAsync(Path.Combine(root, source), """
                #include "literal.h"
                FIXTURE_EXPORT unsigned int LiteralUnit(int kind, int index) {
                    switch (kind) {
                        case 0: return (unsigned char)PLAIN[index];
                        case 1: return (unsigned char)OPAQUE[index];
                        case 2: return (unsigned char)UTF8_TEXT[index];
                        case 3: return (unsigned int)UTF16_TEXT[index];
                        case 4: return (unsigned int)UTF32_TEXT[index];
                        case 5: return (unsigned int)WIDE_TEXT[index];
                        case 6: return (unsigned char)NAMED_ARRAY[index];
                        case 7: return (unsigned char)RAW_TEXT[index];
                        default: return 0xFFFFFFFFU;
                    }
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "literal", nativeName = "literal_fixture" },
                source = new() { language = language, headers = ["literal.h"], includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.Literals", name = "LiteralApi", importMode = importMode,
                    names = ImmutableDictionary<string, string>.Empty.Add("PLAIN", "Plain").Add("OPAQUE", "Opaque")
                        .Add("UTF8_TEXT", "Utf8").Add("UTF16_TEXT", "Utf16").Add("UTF32_TEXT", "Utf32")
                        .Add("WIDE_TEXT", "Wide").Add("NAMED_ARRAY", "Named").Add("RAW_TEXT", "RawText")
                },
                runtime = new() { mode = language == SourceLanguage.Cpp ? "source" : "package" },
                output = new() { layout = language == SourceLanguage.Cpp ? "singleFile" : "split" },
                 cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            Assert.Equal(8, target.interop.stringConstants.Length);
            Assert.DoesNotContain(target.interop.stringConstants, constant => constant.name == "CONTEXT_TEXT");
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            string factory = importMode == BindingImportMode.FunctionTable ? "LiteralApi.Load(binary)" : "LiteralApi.Create()";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, "literal_fixture", $$"""
                using System;
                using System.Linq;
                using System.Text;
                using Generated.Literals;
                public static class LiteralConsumer {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        if (!string.Equals(LiteralApi.Constants.Plain, "A\0BC", StringComparison.Ordinal) || !string.Equals(LiteralApi.Constants.Named, "Q\0R", StringComparison.Ordinal)) return 1;
                        if (!string.Equals(LiteralApi.Constants.Utf8, "😀\0Z", StringComparison.Ordinal) || !string.Equals(LiteralApi.Constants.Utf16, "😀\0Z", StringComparison.Ordinal) || !string.Equals(LiteralApi.Constants.Utf32, "😀\0Z", StringComparison.Ordinal)) return 2;
                        uint[][] units = {
                            LiteralApi.Constants.Plain.Select(c => (uint)c).Append(0U).ToArray(),
                            LiteralApi.Constants.Opaque.ToArray().Select(c => (uint)c).ToArray(),
                            Encoding.UTF8.GetBytes(LiteralApi.Constants.Utf8).Select(c => (uint)c).Append(0U).ToArray(),
                            LiteralApi.Constants.Utf16.Select(c => (uint)c).Append(0U).ToArray(),
                            LiteralApi.Constants.Utf32.EnumerateRunes().Select(r => (uint)r.Value).Append(0U).ToArray(),
                            new uint[LiteralApi.Constants.Wide.Length],
                            LiteralApi.Constants.Named.Select(c => (uint)c).Append(0U).ToArray(),
                            LiteralApi.Constants.RawText.Select(c => (uint)c).Append(0U).ToArray()
                        };
                        for (int i = 0; i < units[5].Length; ++i) units[5][i] = LiteralApi.Constants.Wide[i];
                        for (int kind = 0; kind < units.Length; ++kind)
                            for (int i = 0; i < units[kind].Length; ++i)
                                if (api.LiteralUnit(kind, i) != units[kind][i]) return 10 + kind;
                        if (units[1].Length != 4 || units[1][0] != 255 || units[1][3] != 0) return 3;
                        return 42;
                    }
                }
                """, "LiteralConsumer", root, sourceRuntime: language == SourceLanguage.Cpp));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private const string Header = """
        #pragma once
        #if defined(_WIN32)
        #define FIXTURE_EXPORT __declspec(dllexport)
        #else
        #define FIXTURE_EXPORT __attribute__((visibility("default")))
        #endif
        #define PLAIN "A\0B" "C"
        #define OPAQUE "\xFF\xC3\xA9"
        #define UTF8_TEXT u8"\U0001F600\0Z"
        #define UTF16_TEXT u"\U0001F600\0Z"
        #define UTF32_TEXT U"\U0001F600\0Z"
        #define WIDE_TEXT L"\U0001F600\0Z"
        static const char NAMED_ARRAY[] = "Q\0R";
        #ifdef __cplusplus
        #define RAW_TEXT R"tag(A\nB)tag"
        #else
        #define RAW_TEXT "A\\nB"
        #endif
        #define CONTEXT_TEXT __builtin_FUNCTION()
        #ifdef __cplusplus
        extern "C" {
        #endif
        FIXTURE_EXPORT unsigned int LiteralUnit(int kind, int index);
        #ifdef __cplusplus
        }
        #endif
        """;

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
