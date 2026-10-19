using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCustomFunctionTableTests
{
    [Fact]
    public async Task RuntimeOnlySlotAdapterCallsUnexportedFunctionsAndRetainsModuleUntilLastRealCallReturns()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs custom native table " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string example = Path.Combine(Repository(), "examples", "CustomFunctionTable");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "custom-table", nativeName = "custom_table" },
                source = new() { headers = [Path.Combine(example, "native.h")], includeDirectories = [example], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.CustomFunctionTable", name = "CustomTableApi", importMode = BindingImportMode.FunctionTable,
                    names = ImmutableDictionary<string, string>.Empty.Add("table_add", "Add").Add("table_multiply", "Multiply").Add("table_hold", "Hold")
                },
                runtime = new() { mode = "package" }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult generated = await host.GenerateAsync(new(configuration, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            var selected = Assert.Single(generated.targetModels);
            string native = Path.Combine(example, "native.c"), header = Path.Combine(example, "native.h");
            await NativeConsumerHarness.CompileNativeFixturesAsync(generated, root, configuration.library.nativeName,
                [native], OperatingSystem.IsWindows() ? ["-DBGCS_CUSTOM_TABLE_TEST_FAULTS"] : ["-DBGCS_CUSTOM_TABLE_TEST_FAULTS", "-fvisibility=hidden"]);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(Path.Combine(generated.outputRoot, selected.targetId))));
            Directory.CreateDirectory(Path.Combine(root, "Consumer"));
            File.Copy(Path.Combine(example, "Consumer", "CustomSlotTable.cs"), Path.Combine(root, "Consumer", "CustomSlotTable.cs"));
            object? answer = await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(generated.outputRoot, selected.targetId, "managed"),
                binary, "custom_table", """
                using System;
                using System.Runtime.CompilerServices;
                using System.Runtime.InteropServices;
                using System.Threading;
                using System.Threading.Tasks;
                using BGCS.Runtime;
                using CustomFunctionTable;
                using Generated.CustomFunctionTable;
                using NativeLibrary = System.Runtime.InteropServices.NativeLibrary;
                public static unsafe class CustomTableConsumer {
                    private static readonly ManualResetEventSlim entered = new(false);
                    private static readonly ManualResetEventSlim finish = new(false);
                    private static int callbackTimedOut;
                    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
                    private static void Wait(void* state) {
                        entered.Set();
                        if (!finish.Wait(TimeSpan.FromSeconds(15))) Volatile.Write(ref callbackTimedOut, 1);
                    }
                    private sealed class CountedLease : IDisposable {
                        public int count;
                        public void Dispose() => count++;
                    }
                    public static int Run(string binary) {
                        nint inspect = NativeLibrary.Load(binary);
                        try {
                            if (NativeLibrary.TryGetExport(inspect, "table_add", out _) || NativeLibrary.TryGetExport(inspect, "table_multiply", out _)) return 1;
                            var get = (delegate* unmanaged[Cdecl]<nint*>)NativeLibrary.GetExport(inspect, "custom_table_get");
                            var fault = (delegate* unmanaged[Cdecl]<int, void>)NativeLibrary.GetExport(inspect, "custom_table_set_fault");
                            for (int mode = 1; mode <= 5; mode++) {
                                int released = 0;
                                fault(mode);
                                try { using var invalid = CustomSlotTable.Open(binary, () => Interlocked.Increment(ref released)); return 2; }
                                catch (ArgumentException) { if (released != 1) return 3; }
                                finally { fault(0); }
                            }
                            int releases = 0;
                            var provider = CustomSlotTable.Open(binary, () => Interlocked.Increment(ref releases));
                            using var api = CustomTableApi.FromProvider(provider);
                            nint* slots = get();
                            nint add = slots[6];
                            slots[6] = 0;
                            try {
                                // Provider copied the checked callable, while another read of the now-invalid producer is refused.
                                if (api.Raw.Add(20, 22) != 42 || api.Raw.Multiply(6, 7) != 42) return 4;
                                int failedRelease = 0;
                                try { using var invalid = CustomSlotTable.Open(binary, () => Interlocked.Increment(ref failedRelease)); return 5; }
                                catch (ArgumentException) { if (failedRelease != 1) return 6; }
                            }
                            finally { slots[6] = add; }
                            CountedLease incompleteLease = new();
                            var incomplete = new ExternalFunctionTableProvider(CustomSlotTable.ContractIdentity, CustomSlotTable.ContractIdentity,
                                new[] { "table_add" }, new nint[] { add }, incompleteLease);
                            try { using var partial = CustomTableApi.FromProvider(incomplete); return 7; }
                            catch (EntryPointNotFoundException) { if (incompleteLease.count != 1) return 8; }
                            Task<int> running = Task.Run(() => api.Raw.Hold(&Wait, null));
                            int callFailure = 0;
                            try {
                                if (!entered.Wait(TimeSpan.FromSeconds(10))) callFailure = 9;
                                else {
                                    api.Dispose();
                                    if (Volatile.Read(ref releases) != 0) callFailure = 10;
                                    try { _ = api.Raw.Add(1, 2); callFailure = 11; }
                                    catch (ObjectDisposedException) { }
                                }
                            }
                            finally { finish.Set(); }
                            int result = running.GetAwaiter().GetResult();
                            if (callFailure != 0) return callFailure;
                            if (result != 42 || callbackTimedOut != 0 || Volatile.Read(ref releases) != 1) return 12;
                            api.Dispose(); provider.Dispose();
                            if (releases != 1) return 13;
                            try { _ = provider.TryGetExport("table_add", out _); return 14; }
                            catch (ObjectDisposedException) { }
                            return 42;
                        }
                        finally { NativeLibrary.Free(inspect); }
                    }
                }
                """, "CustomTableConsumer", root, sourceRuntime: false);
            Assert.Equal(42, answer);
            string project = await File.ReadAllTextAsync(Path.Combine(NativeConsumerHarness.NativeDirectory(Path.Combine(generated.outputRoot, selected.targetId)), "Consumer", "Consumer.csproj"));
            Assert.Contains("BGCS.Runtime", project);
            Assert.DoesNotContain("BGCS.Contracts", project);
            Assert.DoesNotContain("BGCS.Frontend", project);
            Assert.DoesNotContain("ClangSharp", project);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static InputDependency Snapshot(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return new(path, BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(path), bytes.LongLength, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))));
    }
    private static string WorkerRoot()
    {
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(Repository(), "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
    private static string Repository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
