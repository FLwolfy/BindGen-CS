using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Targets.Desktop.Targeting;
using BGCS.Targets.Desktop.Toolchains;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeLongDoublePointerTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task NativeAllocatedExtendedValuesUseExactPointerAndDoubleShimsWhileUnprovedValueAbiPreservesPublishedOutput(BindingImportMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-long-double-pointer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string compiler = NativeConsumerHarness.FindCompiler(false);
            await File.WriteAllTextAsync(Path.Combine(root, "extended.h"), """
                #include <stddef.h>
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                API long double* extended_create(double value);
                API void extended_destroy(long double* value);
                API void extended_write(long double* value,double input);
                API double extended_read(const long double* value);
                API double extended_double_shim(double input);
                API size_t extended_size(void);
                API size_t extended_alignment(void);
                API int extended_address_aligned(const long double* value);
                API int extended_live(void);
                API int extended_released(void);
                API long double extended_unproved_value(long double value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "extended.c"), """
                #include "extended.h"
                #include <stdint.h>
                #include <stdlib.h>
                static int live,released;
                API long double* extended_create(double value) {
                    long double* result=(long double*)malloc(sizeof(long double));
                    if(result){*result=(long double)value;++live;}return result;
                }
                API void extended_destroy(long double* value) {if(value){--live;++released;free(value);}}
                API void extended_write(long double* value,double input) {*value=(long double)input*3.0L+0.125L;}
                API double extended_read(const long double* value) {return (double)*value;}
                API double extended_double_shim(double input) {volatile long double value=(long double)input;value=value*2.0L+0.5L;return (double)value;}
                API size_t extended_size(void) {return sizeof(long double);}
                API size_t extended_alignment(void) {return _Alignof(long double);}
                API int extended_address_aligned(const long double* value) {return value && ((uintptr_t)value % _Alignof(long double))==0;}
                API int extended_live(void) {return live;}
                API int extended_released(void) {return released;}
                API long double extended_unproved_value(long double value) {return value+1.0L;}
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "long-double-pointer", nativeName = "long_double_pointer_fixture" },
                source = new()
                {
                    language = SourceLanguage.C,
                    headers = ["extended.h"],
                    parseMacros = false,
                    includeTransitivelyReferencedHeaders = false,
                    selection = new() { excludeDeclarations = ["extended_unproved_value"] }
                },
                targets = [new() { id = "host", settings = new() { compilerPath = compiler } }],
                api = new()
                {
                    @namespace = "Generated.ExtendedPointer",
                    name = "ExtendedApi",
                    importMode = mode,
                    names = ImmutableDictionary<string, string>.Empty.Add("extended_create", "CreateValue").Add("extended_destroy", "DestroyValue")
                        .Add("extended_write", "WriteValue").Add("extended_read", "ReadValue").Add("extended_double_shim", "DoubleShim")
                        .Add("extended_size", "Size").Add("extended_alignment", "Alignment").Add("extended_address_aligned", "AddressAligned")
                        .Add("extended_live", "Live").Add("extended_released", "Released")
                },
                semantics = new()
                {
                    ownedResults = [new() { function = "extended_create", releaseFunction = "extended_destroy",
                    releaseDomain = "extended-native-allocation", releaseThreadDomain = "any" }]
                },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.targetModels);
            NativeType scalar = Assert.Single(target.native.types, type => type.category == NativeTypeCategory.Primitive && type.primitiveKind == "LongDouble");
            Assert.True(scalar.size is > 0);
            Assert.True(scalar.alignment is > 0);
            Assert.DoesNotContain(target.interop.functions, function => function.exportName == "extended_unproved_value");
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string factory = mode == BindingImportMode.FunctionTable ? "ExtendedApi.Load(binary)" : "ExtendedApi.Create()";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"),
                Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot))), configuration.library.nativeName, $$"""
                using System;
                using Generated.ExtendedPointer;
                public static unsafe class ExtendedPointerConsumer {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        if(api.Raw.Size()!=(nuint){{scalar.size}} || api.Raw.Alignment()!=(nuint){{scalar.alignment}})return 1;
                        var value=api.Raw.CreateValue(1.25);
                        if(value==null)return 2;
                        try {
                            if(api.Raw.Live()!=1 || api.Raw.AddressAligned(value)!=1 || api.Raw.ReadValue(value)!=1.25)return 3;
                            api.Raw.WriteValue(value,13.0);
                            if(api.Raw.ReadValue(value)!=39.125)return 4;
                            if(api.Raw.DoubleShim(20.75)!=42.0)return 5;
                        } finally {api.Raw.DestroyValue(value);}
                        if(api.Raw.Live()!=0 || api.Raw.Released()!=1)return 6;
                        return 42;
                    }
                }
                """, "ExtendedPointerConsumer", root, sourceRuntime: false));
            Dictionary<string, string> previous = Snapshot(built.outputRoot);
            BindingConfiguration unproved = configuration with { source = configuration.source with { selection = new() } };
            GenerationResult refused = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(unproved, root));
            Assert.False(refused.success);
            Assert.False(refused.published);
            Assert.Contains("long double", refused.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("calling-ABI", refused.GetDiagnosticSummary(), StringComparison.Ordinal);
            Assert.Equal(previous.OrderBy(item => item.Key), Snapshot(built.outputRoot).OrderBy(item => item.Key));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
