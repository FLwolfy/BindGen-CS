using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCHeaderHelperTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, "package")]
    [InlineData(BindingImportMode.FunctionTable, "source")]
    [InlineData(BindingImportMode.DllImport, "package")]
    [InlineData(BindingImportMode.DllImport, "source")]
    [InlineData(BindingImportMode.LibraryImport, "package")]
    [InlineData(BindingImportMode.LibraryImport, "source")]
    public async Task HeaderHelpersBecomeRealExportsAlongsideExternalCFunctions(BindingImportMode importMode, string runtimeMode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-c-header-helpers 验收 & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "helpers.h"), Header);
            await File.WriteAllTextAsync(Path.Combine(root, "library.c"), "#include \"helpers.h\"\nFIXTURE_EXPORT int External(int value) { return value + 1; }\n");
            BindingConfiguration configuration = Configuration(importMode, runtimeMode);
            await using BindingHost host = Host();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.generation.targetModels);
            Assert.Contains(target.native.declarations, declaration => declaration.name == "Local" && declaration.isStatic && declaration.isDefinition);
            Assert.All(target.interop.functions.Where(function => function.sourceName != "External"), helper => Assert.StartsWith("bgcs_helpers_helper_", helper.exportName));
            Assert.Contains(target.interop.functions, function => function.sourceName == "External" && function.exportName == "External");
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            Assert.True(File.Exists(Path.Combine(targetRoot, "native", "src", "bgcs_helpers_helpers.c")));
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin")));
            object? answer = await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, "helper_fixture", """
                using System;
                using System.Runtime.CompilerServices;
                using System.Runtime.InteropServices;
                using Generated.Helpers;
                public static unsafe class HelperConsumer {
                    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
                    private static int Double(int value) => value * 2;
                    public static int Run(string binary) {
                        using var api = API_FACTORY;
                        int* values = stackalloc int[2] {20, 21};
                        if (api.Raw.HelperAdd(values) != 41 || api.Raw.External(41) != 42 || api.Raw.Local(41) != 42) return 1;
                        Point point = default; point.x = 3; point.y = 4;
                        if (!Convert.ToBoolean(api.Raw.HelperInside(&point))) return 2;
                        delegate* unmanaged[Cdecl]<int, int> callback = &Double;
                        delegate* unmanaged[Cdecl]<int, int> round = api.Raw.HelperRound(callback);
                        if ((nuint)round != (nuint)callback || round(21) != 42) return 3;
                        delegate* unmanaged[Cdecl]<int, int> native = api.Raw.HelperGet();
                        return native != null && native(40) == 42 ? 42 : 4;
                    }
                }
                """.Replace("API_FACTORY", importMode == BindingImportMode.FunctionTable ? "HelperApi.Load(binary)" : "HelperApi.Create()", StringComparison.Ordinal),
                "HelperConsumer", root, sourceRuntime: runtimeMode == "source");
            Assert.Equal(42, answer);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task GenerateOnlyPublishesAReusableNativeBuildContractForHelpers()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-c-helper-generate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "helpers.h"), Header);
            BindingConfiguration configuration = Configuration(BindingImportMode.FunctionTable, "package") with { nativeBuild = new() { mode = "none" } };
            await using BindingHost host = Host();
            GenerationResult result = await host.GenerateAsync(new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(result.targetModels).targetId;
            Assert.True(File.Exists(Path.Combine(result.outputRoot, target, "native", "metadata", "bridge.manifest.json")));
            Assert.True(File.Exists(Path.Combine(result.outputRoot, target, "native", "metadata", "header-helpers.manifest.json")));
            Assert.False(Directory.Exists(Path.Combine(result.outputRoot, target, "native", "bin")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PrebuiltWithoutHelperAdaptersNamesTheRequiredModuleBuildAndPreservesPublication()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-c-helper-prebuilt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "helpers.h"), Header);
            await File.WriteAllTextAsync(Path.Combine(root, "library.c"), "#include \"helpers.h\"\nFIXTURE_EXPORT int External(int value) { return value + 1; }\n");
            BindingConfiguration configuration = Configuration(BindingImportMode.FunctionTable, "package");
            await using BindingHost host = Host();
            BuildResult first = await host.BuildAsync(new(new(configuration with { source = configuration.source with { selection = new() { includeDeclarations = ["External"] } } }, root)));
            Assert.True(first.success, first.GetDiagnosticSummary());
            string target = Assert.Single(first.generation.targetModels).targetId;
            string firstBinary = Assert.Single(Directory.GetFiles(Path.Combine(first.outputRoot, target, "native", "bin")));
            string prebuilt = Path.Combine(root, Path.GetFileName(firstBinary));
            File.Copy(firstBinary, prebuilt);
            var before = Directory.EnumerateFiles(first.outputRoot, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(first.outputRoot, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            BuildResult invalid = await host.BuildAsync(new(new(configuration with
            { nativeBuild = new() { mode = "prebuilt", artifacts = [prebuilt], prebuiltTargetId = target } }, root)));
            Assert.False(invalid.success);
            BindingDiagnostic diagnostic = Assert.Single(invalid.generation.diagnostics, value => value.code == "BGCS-C-INTERNAL-LINKAGE");
            Assert.Contains("helper_fixture", diagnostic.message);
            Assert.Contains("bgcs_helpers_helpers.c", diagnostic.message);
            Assert.NotNull(diagnostic.location);
            var after = Directory.EnumerateFiles(first.outputRoot, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(first.outputRoot, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            Assert.Equal(before.OrderBy(pair => pair.Key), after.OrderBy(pair => pair.Key));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BindingConfiguration Configuration(BindingImportMode importMode, string runtimeMode) => new()
    {
        schemaVersion = 1, library = new() { id = "helpers", nativeName = "helper_fixture" },
        source = new() { language = SourceLanguage.C, headers = ["helpers.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false, includeHeaderHelpers = true },
        api = new() { @namespace = "Generated.Helpers", name = "HelperApi", importMode = importMode },
        runtime = new() { mode = runtimeMode }, nativeBuild = new() { mode = "sources", sources = ["library.c"] }, cache = new() { enabled = false }
    };

    private const string Header = """
        #pragma once
        #if defined(_WIN32)
        #define FIXTURE_EXPORT __declspec(dllexport)
        #else
        #define FIXTURE_EXPORT __attribute__((visibility("default")))
        #endif
        typedef struct Point { int x; int y; } Point;
        typedef int (*Callback)(int value);
        FIXTURE_EXPORT int External(int value);
        static inline int HelperAdd(int values[2]) { return values[0] + values[1]; }
        static inline _Bool HelperInside(const Point* point) { return point->x == 3 && point->y == 4; }
        static int Local(int value) { return value + 1; }
        static inline Callback HelperRound(Callback callback) { return callback; }
        static inline int NativeValue(int value) { return value + 2; }
        static inline int (*HelperGet(void))(int) { return &NativeValue; }
        """;

    private static BindingHost Host() => BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
