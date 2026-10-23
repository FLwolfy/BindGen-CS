using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeDynamicModuleProviderTests
{
    [Fact]
    public async Task AdoptedHandlesActuallyUnloadWhileBorrowedHandlesStayWithTheirOwnerAndLookupCloseIsSerialized()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-module-ownership-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "module.h"), """
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                API int ReadValue(void);
                API void WriteValue(int value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "module.c"), """
                #include "module.h"
                static int stored = 0;
                API int ReadValue(void) { return stored; }
                API void WriteValue(int value) { stored = value; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "module-ownership", nativeName = "module_ownership_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["module.h"], parseMacros = false,
                    includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.ModuleOwnership", name = "ModuleApi" },
                 cache = new() { enabled = false }
            };
            await using BindingHost host = Host();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            string targetRoot = Path.Combine(built.outputRoot, Assert.Single(built.targetModels).targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            // The consumer harness keeps its primary module loaded. This independent
            // copy must reach native reference count zero for reset to be observable.
            string isolated = Path.Combine(root, "isolated-" + Path.GetFileName(binary));
            File.Copy(binary, isolated);
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary,
                configuration.library.nativeName, $$"""
                using System;
                using System.IO;
                using System.Threading;
                using System.Threading.Tasks;
                using BGCS.Runtime;
                using NativeLibrary = System.Runtime.InteropServices.NativeLibrary;
                public static unsafe class ModuleOwnershipConsumer {
                    private static int Read(nint handle) => ((delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(handle, "ReadValue"))();
                    private static void Write(nint handle, int value) => ((delegate* unmanaged[Cdecl]<int, void>)NativeLibrary.GetExport(handle, "WriteValue"))(value);
                    public static int Run(string binary) {
                        string isolated = @"{{isolated.Replace("\"", "\"\"", StringComparison.Ordinal)}}";
                        nint owned = NativeLibrary.Load(isolated);
                        Write(owned, 81);
                        using var adopted = DynamicModuleProvider.FromHandle(owned, ownsHandle: true);
                        if (!adopted.TryGetExport("ReadValue", out nint read) || read == 0) return 1;
                        try { adopted.TryGetExport("", out _); return 2; } catch (ArgumentException) { }
                        if (!adopted.TryGetExport("ReadValue", out _)) return 3;
                        Parallel.For(0, 32, _ => adopted.Dispose());
                        nint afterAdoption = NativeLibrary.Load(isolated);
                        try { if (Read(afterAdoption) != 0) return 4; }
                        finally { NativeLibrary.Free(afterAdoption); }
                        try { adopted.TryGetExport("ReadValue", out _); return 5; } catch (ObjectDisposedException) { }

                        nint borrowed = NativeLibrary.Load(isolated);
                        try {
                            Write(borrowed, 97);
                            using var view = DynamicModuleProvider.FromHandle(borrowed, ownsHandle: false);
                            view.Dispose(); view.Dispose();
                            if (Read(borrowed) != 97) return 6;
                            nint secondReference = NativeLibrary.Load(isolated);
                            try { if (Read(secondReference) != 97) return 7; }
                            finally { NativeLibrary.Free(secondReference); }
                        }
                        finally { NativeLibrary.Free(borrowed); }
                        nint afterBorrow = NativeLibrary.Load(isolated);
                        try { if (Read(afterBorrow) != 0) return 8; }
                        finally { NativeLibrary.Free(afterBorrow); }

                        for (int iteration = 0; iteration < 16; iteration++) {
                            nint handle = NativeLibrary.Load(isolated);
                            var provider = DynamicModuleProvider.FromHandle(handle, ownsHandle: true);
                            using var entered = new ManualResetEventSlim(false);
                            using var lookupStarted = new ManualResetEventSlim(false);
                            Task<int> lookup = Task.Run(() => {
                                entered.Set();
                                int successes = 0;
                                for (;;) {
                                    try {
                                        if (!provider.TryGetExport("ReadValue", out nint address) || address == 0) return -1;
                                        successes++; lookupStarted.Set();
                                    } catch (ObjectDisposedException) { return successes; }
                                }
                            });
                            try {
                                if (!entered.Wait(TimeSpan.FromSeconds(10)) || !lookupStarted.Wait(TimeSpan.FromSeconds(10))) return 9;
                                Parallel.For(0, 16, _ => provider.Dispose());
                                if (lookup.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() <= 0) return 10;
                                try { provider.TryGetExport("ReadValue", out _); return 11; } catch (ObjectDisposedException) { }
                            }
                            finally {
                                provider.Dispose();
                                lookup.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                            }
                        }
                        return 42;
                    }
                }
                """, "ModuleOwnershipConsumer", root, sourceRuntime: false));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BindingHost Host()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return BindingGenerator.CreateBuilder().UseWorkerDirectory(Path.Combine(root!.FullName,
            "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0")).Build();
    }
}
