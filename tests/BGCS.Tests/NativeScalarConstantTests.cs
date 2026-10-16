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

public sealed class NativeScalarConstantTests
{
    [Theory]
    [InlineData(SourceLanguage.C, BindingImportMode.FunctionTable)]
    [InlineData(SourceLanguage.C, BindingImportMode.DllImport)]
    [InlineData(SourceLanguage.C, BindingImportMode.LibraryImport)]
    [InlineData(SourceLanguage.Cpp, BindingImportMode.FunctionTable)]
    [InlineData(SourceLanguage.Cpp, BindingImportMode.DllImport)]
    [InlineData(SourceLanguage.Cpp, BindingImportMode.LibraryImport)]
    public async Task GeneratedConstantsMatchIndependentNativeEvaluationAcrossAllImports(SourceLanguage language, BindingImportMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs native constants " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "constants.h"), """
                #include <stdint.h>
                #define MAXIMUM 18446744073709551615ULL
                #define SHIFTED ((1ULL << 63) | 17ULL)
                #define MINIMUM (-9223372036854775807LL - 1LL)
                #define NEGATIVE_ZERO (-0.0f)
                #define HALF (1.0 / 2.0)
                #define BAD_TYPE unsigned int
                #define BAD_STATEMENT {
                #define REMOVED 7
                #undef REMOVED
                static const unsigned long long FIELD_VALUE = SHIFTED;
                uint64_t read_maximum(void);
                uint64_t read_shifted(void);
                int64_t read_minimum(void);
                uint32_t read_zero_bits(void);
                double read_half(void);
                """);
            string extension = language == SourceLanguage.C ? "c" : "cpp";
            await File.WriteAllTextAsync(Path.Combine(root, "constants." + extension), """
                #include "constants.h"
                #include <string.h>
                #if defined(_WIN32)
                #define EXPORT __declspec(dllexport)
                #else
                #define EXPORT __attribute__((visibility("default")))
                #endif
                EXPORT uint64_t read_maximum(void) { return MAXIMUM; }
                EXPORT uint64_t read_shifted(void) { return SHIFTED; }
                EXPORT int64_t read_minimum(void) { return MINIMUM; }
                EXPORT uint32_t read_zero_bits(void) { float value = NEGATIVE_ZERO; uint32_t bits; memcpy(&bits, &value, sizeof(bits)); return bits; }
                EXPORT double read_half(void) { return HALF; }
                """);
            bool sourceRuntime = mode == BindingImportMode.LibraryImport;
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "constant_fixture", nativeName = "constant_fixture" },
                source = new() { language = language, headers = ["constants.h"], parseMacros = true, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.ConstantFixture", name = "ConstantApi", importMode = mode,
                    names = ImmutableDictionary<string, string>.Empty.Add("MAXIMUM", "Maximum").Add("SHIFTED", "Shifted").Add("MINIMUM", "Minimum")
                        .Add("NEGATIVE_ZERO", "NegativeZero").Add("HALF", "Half").Add("FIELD_VALUE", "FieldValue") },
                runtime = new() { mode = sourceRuntime ? "source" : "package" }, output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                nativeBuild = new() { mode = "sources", sources = ["constants." + extension] }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(target, "native", "bin")));
            string factory = mode == BindingImportMode.FunctionTable ? "ConstantApi.Load(binary)" : "ConstantApi.Create()";
            string probe = $$"""
                using System;
                using Generated.ConstantFixture;
                public static class ConstantProbe {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        if (api.ReadMaximum() != ConstantApi.Constants.Maximum || api.ReadMaximum() != ulong.MaxValue) return 1;
                        if (api.ReadShifted() != ConstantApi.Constants.Shifted || ConstantApi.Constants.FieldValue != api.ReadShifted()) return 2;
                        if (api.ReadMinimum() != ConstantApi.Constants.Minimum || api.ReadMinimum() != long.MinValue) return 3;
                        if (api.ReadZeroBits() != BitConverter.SingleToUInt32Bits(ConstantApi.Constants.NegativeZero) || api.ReadZeroBits() != 0x80000000U) return 4;
                        if (api.ReadHalf() != ConstantApi.Constants.Half || BitConverter.DoubleToUInt64Bits(ConstantApi.Constants.Half) != 0x3FE0000000000000UL) return 5;
                        if (typeof(ConstantApi.Constants).GetField("Removed") != null || typeof(ConstantApi.Constants).GetField("BadType") != null) return 6;
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(target, "managed"), binary, "constant_fixture", probe, "ConstantProbe", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
