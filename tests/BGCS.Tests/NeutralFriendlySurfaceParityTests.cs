using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NeutralFriendlySurfaceParityTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task RenamedContainersTextSpansDirectedValuesAndUnprovenRawPointersCoexist(BindingImportMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-friendly-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "friendly.h"), Header);
            await File.WriteAllTextAsync(Path.Combine(root, "friendly.c"), Native);
            bool sourceRuntime = mode == BindingImportMode.LibraryImport;
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "friendly-parity", nativeName = "friendly_parity_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["friendly.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.FriendlyParity",
                    name = "FriendlyApi",
                    importMode = mode,
                    naming = new() { functions = new() { casing = ManagedNameCasing.Pascal, removePrefixes = ["bgcs_"] } },
                    names = ImmutableDictionary<string, string>.Empty.Add("bgcs_add::left", "x").Add("bgcs_read::value", "result"),
                    functionContainers = [new("Math", [new("bgcs_add", "Add")])],
                    functionAliases = [new("bgcs_name", "Label")],
                    functions = [new("bgcs_add") { defaults = [new("right", new(ManagedDefaultValueKind.Integer, "2"))] }]
                },
                semantics = new()
                {
                    stringParameters = [new()
                    {
                        function = "bgcs_name_length", parameter = "name", encoding = NativeTextEncoding.Utf8, codeUnitWidth = 1,
                        byteOrder = NativeTextByteOrder.Native, lengthUnit = NativeLengthUnit.CodeUnits, direction = NativeParameterDirection.Input,
                        lifetime = NativeLifetimeKind.Call, zeroTerminated = true, allowsNull = false, retainsPointer = false
                    }],
                    borrowedResults = [new() { function = "bgcs_name", lifetime = NativeLifetimeKind.Module }],
                    stringResults = [new() { function = "bgcs_name", encoding = NativeTextEncoding.Utf8, zeroTerminated = true, allowsNull = false }],
                    bufferParameters = [new()
                    {
                        function = "bgcs_sum", parameter = "values", lengthParameter = "count", lengthUnit = NativeLengthUnit.Elements,
                        direction = NativeParameterDirection.Input, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = true
                    }],
                    valueParameters = [new()
                    {
                        function = "bgcs_read", parameter = "value", direction = NativeParameterDirection.Output,
                        lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false
                    }]
                },
                cache = new() { enabled = false },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.targetModels);
            Assert.Equal(8, target.interop.functions.Length);
            Assert.Equal("bgcs_add", Assert.Single(target.interop.functions, function => function.exportName == "bgcs_add").exportName);
            Assert.Equal("x", Assert.Single(target.managed!.methods, method => method.name == "Add").parameterNames[0]);
            Assert.Contains(built.diagnostics, diagnostic => diagnostic.code == BindingDiagnosticCodes.C_OWNERSHIP
                && diagnostic.severity == BindingDiagnosticSeverity.Warning && diagnostic.message.Contains("bgcs_unproven", StringComparison.Ordinal));
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string managedRoot = Path.Combine(targetRoot, "managed");
            string sources = string.Join("\n", Directory.GetFiles(managedRoot, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.Contains("public int Add(int x, int right)", sources, StringComparison.Ordinal);
            Assert.Contains("public void Read(out int result)", sources, StringComparison.Ordinal);
            Assert.Contains("public int NameLength(string name)", sources, StringComparison.Ordinal);
            Assert.Contains("public void FarSize(int size, int kind, int mode, int* data)", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("FarSize(int kind, int mode,", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("string Unproven(", sources, StringComparison.Ordinal);
            string factory = mode == BindingImportMode.FunctionTable ? "FriendlyApi.Load(binary)" : "FriendlyApi.Create()";
            string consumer = $$"""
                using System;
                using Generated.FriendlyParity;
                public static unsafe class FriendlyParityConsumer {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        var math = api.Math;
                        if (api.Add(x:40,right:2)!=42 || api.Add(x:40)!=42 || math.Add(x:40,right:2)!=42 || math.Add(x:40)!=42) return 1;
                        if (api.NameLength("A😀B")!=6 || api.Name()!="BGCS" || api.Label()!="BGCS") return 2;
                        api.Read(out int result);
                        if(result!=42) return 3;
                        ReadOnlySpan<int> values=stackalloc int[]{19,23};
                        api.Sum(values);
                        if(api.Raw.LastSum()!=42) return 4;
                        api.Sum(ReadOnlySpan<int>.Empty);
                        if(api.Raw.LastSum()!=0) return 5;
                        int data=2;
                        // An undeclared pointer protocol stays available through the exact raw API.
                        api.Raw.FarSize(size:10,kind:20,mode:10,data:&data);
                        if(api.Raw.LastSum()!=42 || api.Raw.Unproven()[0]!=(byte)'B') return 6;
                        api.Raw.Read(&data);
                        if(data!=42 || api.Raw.Add(x:40,right:2)!=42) return 7;
                        api.Dispose();
                        try{math.Add(40,2);return 8;}catch(ObjectDisposedException){}
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(managedRoot,
                Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot))), configuration.library.nativeName,
                consumer, "FriendlyParityConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private const string Header = """
        #if defined(_WIN32)
        #define API __declspec(dllexport)
        #else
        #define API __attribute__((visibility("default")))
        #endif
        API int bgcs_add(int left, int right);
        API int bgcs_name_length(const char* name);
        API const char* bgcs_name(void);
        API const char* bgcs_unproven(void);
        API void bgcs_sum(const int* values, int count);
        API int bgcs_last_sum(void);
        API void bgcs_far_size(int size, int kind, int mode, const int* data);
        API void bgcs_read(int* value);
        """;

    private const string Native = """
        #include "friendly.h"
        static int last_sum;
        API int bgcs_add(int left, int right) { return left+right; }
        API int bgcs_name_length(const char* name) { int count=0;while(name[count])++count;return count; }
        API const char* bgcs_name(void) { return "BGCS"; }
        API const char* bgcs_unproven(void) { return "BGCS"; }
        API void bgcs_sum(const int* values, int count) { last_sum=0;for(int i=0;i<count;++i)last_sum+=values[i]; }
        API int bgcs_last_sum(void) { return last_sum; }
        API void bgcs_far_size(int size, int kind, int mode, const int* data) { last_sum=size+kind+mode+*data; }
        API void bgcs_read(int* value) { *value=42; }
        """;

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
