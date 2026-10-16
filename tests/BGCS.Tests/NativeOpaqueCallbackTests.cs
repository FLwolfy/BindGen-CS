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

public sealed class NativeOpaqueCallbackTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task VariadicCallbackAddressesRoundTripWithoutGeneratedManagedInvocation(BindingImportMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs opaque callbacks " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "callbacks.h"), """
                typedef int (*variadic_callback)(int first, ...);
                struct CallbackTable { int tag; variadic_callback callback; };
                void fill_table(struct CallbackTable* table);
                variadic_callback get_callback(void);
                int native_invoke(variadic_callback callback);
                int native_invoke_table(const struct CallbackTable* table);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "callbacks.c"), """
                #include "callbacks.h"
                #include <stdarg.h>
                #if defined(_WIN32)
                #define EXPORT __declspec(dllexport)
                #else
                #define EXPORT __attribute__((visibility("default")))
                #endif
                static int add_values(int first, ...) {
                    va_list args; va_start(args, first);
                    int result = first + va_arg(args, int) + va_arg(args, int);
                    va_end(args); return result;
                }
                EXPORT void fill_table(struct CallbackTable* table) { table->tag = 7; table->callback = &add_values; }
                EXPORT variadic_callback get_callback(void) { return &add_values; }
                EXPORT int native_invoke(variadic_callback callback) { return callback(7, 11, 24); }
                EXPORT int native_invoke_table(const struct CallbackTable* table) { return table->callback(table->tag, 11, 24); }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "callback_fixture", nativeName = "callback_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["callbacks.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.OpaqueCallbacks", name = "CallbackApi", importMode = mode },
                runtime = new() { mode = mode == BindingImportMode.LibraryImport ? "source" : "package" },
                nativeBuild = new() { mode = "sources", sources = ["callbacks.c"] }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string factory = mode == BindingImportMode.FunctionTable ? "CallbackApi.Load(binary)" : "CallbackApi.Create()";
            string probe = $$"""
                using Generated.OpaqueCallbacks;
                public static unsafe class OpaqueCallbackProbe {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        CallbackTable table = default;
                        api.Raw.FillTable(&table);
                        nint address = api.Raw.GetCallback();
                        if (address == 0 || table.callback != address) return 1;
                        if (api.Raw.NativeInvoke(address) != 42 || api.Raw.NativeInvokeTable(&table) != 42) return 2;
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(target, "managed"),
                Assert.Single(Directory.GetFiles(Path.Combine(target, "native", "bin"))), "callback_fixture", probe, "OpaqueCallbackProbe", root,
                configuration.runtime.mode == "source"));
            string metadata = await File.ReadAllTextAsync(Path.Combine(target, "metadata", "abi.manifest.json"));
            Assert.Contains("opaqueCallbackAddresses", metadata, StringComparison.Ordinal);
            Assert.Contains("native adapter", metadata, StringComparison.Ordinal);
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
