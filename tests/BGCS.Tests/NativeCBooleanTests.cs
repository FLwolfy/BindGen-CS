using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCBooleanTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task CBooleanValuesNormalizeAtFriendlyBoundaryAndPreserveByteStorage(BindingImportMode mode, bool sourceRuntime)
        => await ExerciseAsync(mode, sourceRuntime, false);

    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task NestedCarriersPreserveRealNativeCallsAcrossImportRuntimeAndApiStyles(BindingImportMode mode, bool sourceRuntime)
        => await ExerciseAsync(mode, sourceRuntime, true);

    private static async Task ExerciseAsync(BindingImportMode mode, bool sourceRuntime, bool nested)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-c-boolean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "boolean.h"), """
                #if defined(_WIN32)
                #define EXPORT __declspec(dllexport)
                #else
                #define EXPORT __attribute__((visibility("default")))
                #endif
                typedef _Bool Flag;
                typedef struct FlagRecord { Flag enabled; unsigned char marker; } FlagRecord;
                EXPORT Flag negate(Flag value);
                EXPORT unsigned char byte_identity(unsigned char value);
                EXPORT void store_flag(FlagRecord* record, Flag value);
                EXPORT int custom_flag(int value);
                EXPORT float scalar_vector(float value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "boolean.c"), """
                #include "boolean.h"
                EXPORT Flag negate(Flag value) { return !value; }
                EXPORT unsigned char byte_identity(unsigned char value) { return value; }
                EXPORT void store_flag(FlagRecord* record, Flag value) { record->enabled = value; record->marker = 42; }
                EXPORT int custom_flag(int value) { return value; }
                EXPORT float scalar_vector(float value) { return value * 2; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "c_boolean", nativeName = "c_boolean_fixture" },
                source = new() { headers = ["boolean.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.CBoolean", name = "BooleanApi", importMode = mode,
                    nestGeneratedTypesInApi = nested,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    functionAliases = [new("negate", "Invert")], functionContainers = [new("Flags", [new("negate", "Not")])],
                    records = [new("FlagRecord") { name = "FlagCarrier", documentation = "The exact native flag carrier < 3 bytes.",
                        fields = [new("enabled") { name = "Enabled" }, new("marker") { name = "Marker" }],
                        emitSizeConstant = true, constructorFields = ["enabled", "marker"],
                        validity = new("enabled", new(ManagedDefaultValueKind.Integer, "0"), "IsEnabled") }],
                    functions =
                    [
                        new("custom_flag") { parameters = [new("value", new("bool", "{value} ? 17 : 0"))],
                            returns = new("bool", null, "{value} != 0"), defaults = [new("value", new(ManagedDefaultValueKind.Boolean, "false"))],
                            inputVariations = [new([new("value", new("int", "{value} != 0"))]),
                                new([new("value", new("long", "{value} != 0"))]) { name = "CustomFlagFromLong" }] },
                        new("scalar_vector") { parameters = [new("value", new("Vector2", "{value}.X + {value}.Y", requiredNamespace: "System.Numerics"))],
                            returns = new("Vector2", null, "new Vector2({value}, -{value})", "System.Numerics"),
                            inputVariations = [new([new("value", new("Vector3", "new global::System.Numerics.Vector2({value}.X, {value}.Y)", requiredNamespace: "System.Numerics"))])] }
                    ] },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                semantics = new() { valueParameters = [new() { function = "store_flag", parameter = "record",
                    direction = NativeParameterDirection.InputOutput, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false }] },
                nativeBuild = new() { mode = "sources", sources = ["boolean.c"] }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.generation.targetModels);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin")));
            Assert.Equal(5, target.interop!.functions.Length);
            Assert.Equal(4, target.managed!.methods.Count(method => !method.valueProjections.IsEmpty));
            string concrete = sourceRuntime ? "BooleanApiContext" : "BooleanApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string sharedFactory = mode == BindingImportMode.FunctionTable ? "BooleanApi.Load(binary);" : "BooleanApi.Create();";
            string shared = sourceRuntime ? sharedFactory + "\nif (!BooleanApi.Negate(false) || !BooleanApi.Flags.Not(false)) return 6;\nBooleanApi.Dispose();" : "";
            string consumer = $$"""
                using System;
                using System.Numerics;
                using Generated.CBoolean;
                using ApiContext = Generated.CBoolean.{{concrete}};
                {{(nested ? "using FlagCarrier = Generated.CBoolean.BooleanApi.FlagCarrier;" : string.Empty)}}
                public static unsafe class CBooleanProbe {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        if (!api.Negate(false) || api.Negate(true) || !api.Invert(false) || api.Flags.Not(true)) return 1;
                        if (api.Raw.Negate(0) != 1 || api.Raw.Negate(1) != 0 || api.ByteIdentity(255) != 255) return 2;
                        FlagCarrier record = new(0, 7);
                        api.StoreFlag(ref record, true);
                        if (sizeof(FlagCarrier) != FlagCarrier.NativeSize || sizeof(FlagCarrier) != 2 || !record.IsEnabled || record.Enabled != 1 || record.Marker != 42) return 3;
                        api.StoreFlag(ref record, false);
                        if (record.IsEnabled || record.Enabled != 0 || record.Marker != 42) return 4;
                        if (!api.CustomFlag(true) || api.CustomFlag(false) || api.CustomFlag() || api.Raw.CustomFlag(-7) != -7) return 7;
                        if (!api.CustomFlag(7) || api.CustomFlag(0) || !api.CustomFlagFromLong(long.MinValue)) return 9;
                        Vector2 vector = api.ScalarVector(new Vector2(2, 3));
                        if (vector.X != 10 || vector.Y != -10 || api.Raw.ScalarVector(5) != 10) return 8;
                        Vector2 alternate = api.ScalarVector(new Vector3(2, 3, 99));
                        if (alternate.X != 10 || alternate.Y != -10) return 10;
                        {{shared}}
                        api.Dispose();
                        try { _ = api.Negate(true); return 5; } catch (ObjectDisposedException) { }
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary,
                configuration.library.nativeName, consumer, "CBooleanProbe", root, sourceRuntime));
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
