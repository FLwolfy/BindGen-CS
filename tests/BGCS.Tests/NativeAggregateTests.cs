using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeAggregateTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task GeneratedAggregateCallsPreserveNativeArgumentsReturnsAlignmentAndClassification(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs aggregate " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "aggregates.h"), Header);
            await File.WriteAllTextAsync(Path.Combine(root, "aggregates.c"), NativeSource);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "aggregate_fixture", nativeName = "aggregate_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["aggregates.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.AggregateFixture", name = "AggregateApi", importMode = mode },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime && mode == BindingImportMode.LibraryImport ? "singleFile" : "split" },
                 cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string factory = mode == BindingImportMode.FunctionTable ? "AggregateApi.Load(binary)" : "AggregateApi.Create()";
            string probe = $$"""
                using Generated.AggregateFixture;
                public static unsafe class AggregateProbe {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        Small small = api.TransformSmall(new Small { a = 1, b = 2, c = 3 });
                        if (small.a != 4 || small.b != 7 || small.c != 10) return 1;
                        Pair pair = api.TransformPair(new Pair { a = 17, b = 1.25f });
                        if (pair.a != 21 || pair.b != 2.5f) return 2;
                        Mixed mixed = api.TransformMixed(new Mixed { a = 3.5, b = 19 });
                        if (mixed.a != 10.5 || mixed.b != 28) return 3;
                        Large large = api.Raw.TransformLarge(new Large { a = 0xFEDCBA9876543210UL, b = 5, c = 2.25, d = 7 });
                        if (large.a != (0xFEDCBA9876543210UL ^ 0x8000000000000001UL) || large.b != 14 || large.c != 4.5 || large.d != 20) return 4;
                        Number number = api.TransformNumber(new Number { bits = 0xFEDCBA9876543210UL });
                        if (number.bits != (0xFEDCBA9876543210UL ^ 0x8000000000000001UL)) return 5;
                        Nested nested = api.TransformNested(new Nested { prefix = 3, nested = new Pair { a = 19, b = 2.5f }, tail = 4.25 });
                        if (nested.prefix != 8 || nested.nested.a != 23 || nested.nested.b != 5.0f || nested.tail != 8.5) return 6;
                        Floats floats = api.TransformFloats(new Floats { a = 1.25f, b = 2.5f, c = 3.75f, d = 5.0f });
                        if (floats.a != 2.5f || floats.b != 7.5f || floats.c != 15.0f || floats.d != 25.0f) return 7;
                        Doubles doubles = api.TransformDoubles(new Doubles { a = 1.25, b = 2.5 });
                        if (doubles.a != 5.0 || doubles.b != 12.5) return 8;
                        ArrayPacket array = default; array.values[0] = 7; array.values[1] = 11; array.values[2] = 13; array.tag = 2;
                        array = api.TransformArray(array);
                        if (array.values[0] != 14 || array.values[1] != 33 || array.values[2] != 52 || array.tag != 9) return 9;
                        Packed packed = api.TransformPacked(new Packed { prefix = 2, value = 4.25 });
                        if (packed.prefix != 9 || packed.value != 17.0) return 10;
                        if (api.Raw.TransformPair(pair).a != 25) return 11;
                        Pair callback = api.Raw.InvokePairCallback(new Pair { a = 7, b = 1.5f }, &MutatePair);
                        if (callback.a != 24 || callback.b != 9.0f) return 12;
                        FloatOne oneFloat = api.TransformFloatOne(new FloatOne { value = 1.5f });
                        DoubleOne oneDouble = api.TransformDoubleOne(new DoubleOne { value = 1.25 });
                        if (oneFloat.value != 6.0f || oneDouble.value != 10.0) return 13;
                        return 42;
                    }
                    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
                    private static Pair MutatePair(Pair value) { value.a += 8; value.b *= 3; return value; }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(target, "managed"),
                Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target))), "aggregate_fixture", probe, "AggregateProbe", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private const string Header = """
        #include <stdint.h>
        typedef struct Small { uint8_t a, b, c; } Small;
        typedef struct Pair { int32_t a; float b; } Pair;
        typedef struct Mixed { double a; int32_t b; } Mixed;
        typedef struct Large { uint64_t a; int32_t b; double c; uint8_t d; } Large;
        typedef union Number { double real; uint64_t bits; } Number;
        typedef struct Nested { uint8_t prefix; Pair nested; double tail; } Nested;
        typedef struct Floats { float a, b, c, d; } Floats;
        typedef struct Doubles { double a, b; } Doubles;
        typedef struct ArrayPacket { uint16_t values[3]; uint8_t tag; } ArrayPacket;
        typedef struct FloatOne { float value; } FloatOne;
        typedef struct DoubleOne { double value; } DoubleOne;
        #pragma pack(push, 1)
        typedef struct Packed { uint8_t prefix; double value; } Packed;
        #pragma pack(pop)
        Small transform_small(Small value);
        Pair transform_pair(Pair value);
        Mixed transform_mixed(Mixed value);
        Large transform_large(Large value);
        Number transform_number(Number value);
        Nested transform_nested(Nested value);
        Floats transform_floats(Floats value);
        Doubles transform_doubles(Doubles value);
        ArrayPacket transform_array(ArrayPacket value);
        Packed transform_packed(Packed value);
        typedef Pair (*pair_callback)(Pair value);
        Pair invoke_pair_callback(Pair value, pair_callback callback);
        FloatOne transform_float_one(FloatOne value);
        DoubleOne transform_double_one(DoubleOne value);
        """;

    private const string NativeSource = """
        #include "aggregates.h"
        #if defined(_WIN32)
        #define EXPORT __declspec(dllexport)
        #else
        #define EXPORT __attribute__((visibility("default")))
        #endif
        EXPORT Small transform_small(Small value) { value.a += 3; value.b += 5; value.c += 7; return value; }
        EXPORT Pair transform_pair(Pair value) { value.a += 4; value.b *= 2; return value; }
        EXPORT Mixed transform_mixed(Mixed value) { value.a *= 3; value.b += 9; return value; }
        EXPORT Large transform_large(Large value) { value.a ^= UINT64_C(0x8000000000000001); value.b += 9; value.c *= 2; value.d += 13; return value; }
        EXPORT Number transform_number(Number value) { value.bits ^= UINT64_C(0x8000000000000001); return value; }
        EXPORT Nested transform_nested(Nested value) { value.prefix += 5; value.nested = transform_pair(value.nested); value.tail *= 2; return value; }
        EXPORT Floats transform_floats(Floats value) { value.a *= 2; value.b *= 3; value.c *= 4; value.d *= 5; return value; }
        EXPORT Doubles transform_doubles(Doubles value) { value.a *= 4; value.b *= 5; return value; }
        EXPORT ArrayPacket transform_array(ArrayPacket value) { value.values[0] *= 2; value.values[1] *= 3; value.values[2] *= 4; value.tag += 7; return value; }
        EXPORT Packed transform_packed(Packed value) { value.prefix += 7; value.value *= 4; return value; }
        EXPORT Pair invoke_pair_callback(Pair value, pair_callback callback) { value = callback(transform_pair(value)); value.a += 5; return value; }
        EXPORT FloatOne transform_float_one(FloatOne value) { value.value *= 4; return value; }
        EXPORT DoubleOne transform_double_one(DoubleOne value) { value.value *= 8; return value; }
        """;

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
