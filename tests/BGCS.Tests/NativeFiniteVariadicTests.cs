using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeFiniteVariadicTests
{
    [Theory]
    [InlineData(SourceLanguage.C, BindingImportMode.FunctionTable)]
    [InlineData(SourceLanguage.C, BindingImportMode.DllImport)]
    [InlineData(SourceLanguage.C, BindingImportMode.LibraryImport)]
    [InlineData(SourceLanguage.Cpp, BindingImportMode.FunctionTable)]
    [InlineData(SourceLanguage.Cpp, BindingImportMode.DllImport)]
    [InlineData(SourceLanguage.Cpp, BindingImportMode.LibraryImport)]
    public async Task RealNativeFixedAdaptersApplyDefaultPromotionsAndRetainTheOriginalEllipsisFacts(SourceLanguage language, BindingImportMode importMode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-finite-varargs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "variadic.h"), Header);
            string nativeSource = "variadic." + (language == SourceLanguage.C ? "c" : "cpp");
            await File.WriteAllTextAsync(Path.Combine(root, nativeSource), """
                #include "variadic.h"
                #include <stdarg.h>
                FIXTURE_EXPORT long long Analyze(int marker, ...) {
                    va_list values; va_start(values, marker);
                    int narrow = va_arg(values, int);
                    double real = va_arg(values, double);
                    int flag = va_arg(values, int);
                    intptr_t wide = va_arg(values, intptr_t);
                    const int* pointer = va_arg(values, const int*);
                    va_end(values);
                    return marker + narrow + (int)real + flag + (long long)wide + *pointer;
                }
                """);
            BindingConfiguration configuration = Configuration(language, importMode) with { };
            ConcurrentQueue<SourceLanguage> parses = new();
            await using BindingHost host = BindingGenerator.CreateBuilder().ObserveWorkerParses(WorkerRoot(), parses).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            Assert.Equal(new[] { language, SourceLanguage.C }, parses.ToArray());
            TargetInspection target = Assert.Single(result.targetModels);
            Assert.True(Assert.Single(target.native.declarations, declaration => declaration.kind == "Function" && declaration.name == "Analyze" && declaration.isVariadic).isVariadic);
            Assert.Contains(target.interop.functions, function => function.sourceName == "AnalyzeValues" && !function.isVariadic);
            Assert.DoesNotContain(target.interop.functions, function => function.isVariadic || function.sourceName == "Analyze");
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            Assert.True(File.Exists(Path.Combine(targetRoot, "native", "metadata", "variadic.adapters.json")));
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            string factory = importMode == BindingImportMode.FunctionTable ? "VariadicApi.Load(binary)" : "VariadicApi.Create()";
            string call = language == SourceLanguage.C
                ? "return api.Raw.AnalyzeValues(1, -3, 4.5f, (VariantFlag)7, 9, &value) == 42 ? 42 : 1;"
                : "long result = 0; return api.Raw.AnalyzeValues(1, -3, 4.5f, (VariantFlag)7, 9, &value, &result, null) == 0 && result == 42 ? 42 : 1;";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, "variadic_fixture", $$"""
                using Generated.Variadics;
                public static unsafe class VariadicConsumer {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        int value = 24;
                        {{call}}
                    }
                }
                """, "VariadicConsumer", root, sourceRuntime: language == SourceLanguage.Cpp));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task ExplicitFiniteVariantsRemainPublicWhenOrdinaryDeclarationFiltersSelectOtherFunctions(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-selected-varargs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "variadic.h"), Header + "\nFIXTURE_EXPORT int Unrelated(void);\n");
            BindingConfiguration configuration = Configuration(language, BindingImportMode.FunctionTable);
            configuration = configuration with { source = configuration.source with
                { selection = new() { includeDeclarations = ["Unrelated"] } } };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await host.GenerateAsync(new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            var module = Assert.Single(result.targetModels).interop;
            Assert.Contains(module.functions, function => function.sourceName == "Unrelated");
            Assert.Contains(module.functions, function => function.sourceName == "AnalyzeValues" && !function.isVariadic);
            Assert.DoesNotContain(module.functions, function => function.sourceName == "Analyze" || function.isVariadic);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task UnconfiguredSelectedVariadicDeclarationsRemainExplicitlyUnsupported(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-unconfigured-varargs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "variadic.h"), Header);
            BindingConfiguration configuration = Configuration(language, BindingImportMode.FunctionTable);
            configuration = configuration with { source = configuration.source with { variadicVariants = [] } };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await host.GenerateAsync(new(configuration, root));
            Assert.False(result.success);
            string reason = result.GetDiagnosticSummary();
            Assert.True(reason.Contains("fixed", StringComparison.OrdinalIgnoreCase) || reason.Contains("variadic", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("vararg", StringComparison.OrdinalIgnoreCase), reason);
            Assert.False(Directory.Exists(Path.Combine(root, "Generated")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BindingConfiguration Configuration(SourceLanguage language, BindingImportMode importMode) => new()
    {
        schemaVersion = 2, library = new() { id = "finitevarargs", nativeName = "variadic_fixture" },
        source = new()
        {
            language = language, headers = ["variadic.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false,
            selection = new() { includeDeclarations = ["Analyze(int, ...)"] },
            variadicVariants = [new()
            {
                function = "Analyze(int, ...)", name = "AnalyzeValues", parameters =
                [new("narrow", "signed char"), new("real", "float"), new("flag", "VariantFlag"), new("wide", "intptr_t"), new("pointer", "const int*")]
            }]
        },
        api = new() { @namespace = "Generated.Variadics", name = "VariadicApi", importMode = importMode },
        runtime = new() { mode = language == SourceLanguage.Cpp ? "source" : "package" },
        output = new() { layout = language == SourceLanguage.Cpp ? "singleFile" : "split" },
        cache = new() { enabled = false }
    };

    private const string Header = """
        #pragma once
        #include <stdint.h>
        #if defined(_WIN32)
        #define FIXTURE_EXPORT __declspec(dllexport)
        #else
        #define FIXTURE_EXPORT __attribute__((visibility("default")))
        #endif
        typedef enum VariantFlag { VariantFlagValue = 7 } VariantFlag;
        #ifdef __cplusplus
        extern "C" {
        #endif
        FIXTURE_EXPORT long long Analyze(int marker, ...);
        #ifdef __cplusplus
        }
        inline long long Analyze(int marker, signed char narrow, float real, VariantFlag flag, intptr_t wide, const int* pointer) {
            (void)marker; (void)narrow; (void)real; (void)flag; (void)wide; (void)pointer;
            return -991;
        }
        #endif
        """;

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
