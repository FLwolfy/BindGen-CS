using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeExternalAbiPolicyTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task AlignedPointerShimRemainsUsableWhileExternalFieldsCannotBypassUnsupportedValueAbi(BindingImportMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-external-abi-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "point.h"), """
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #define ALIGNED(n) __declspec(align(n))
                #else
                #define API __attribute__((visibility("default")))
                #define ALIGNED(n) __attribute__((aligned(n)))
                #endif
                typedef struct ALIGNED(16) Point { float x; float y; } Point;
                API Point by_value(Point value);
                API void copy_point(const Point* input,Point* output);
                API float read_point(const Point* value);
                API int point_size(void);
                API int point_alignment(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "point.c"), """
                #include "point.h"
                API Point by_value(Point value) { return value; }
                API void copy_point(const Point* input,Point* output) { output->x=input->x+10;output->y=input->y+20; }
                API float read_point(const Point* value) { return value->x+value->y; }
                API int point_size(void) { return sizeof(Point); }
                API int point_alignment(void) { return _Alignof(Point); }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "external-policy", nativeName = "external_policy_fixture" },
                source = new()
                {
                    language = SourceLanguage.C,
                    headers = ["point.h"],
                    parseMacros = false,
                    includeTransitivelyReferencedHeaders = false,
                    selection = new() { excludeDeclarations = ["by_value"] }
                },
                api = new()
                {
                    @namespace = "Generated.ExternalPolicyNative",
                    name = "PointApi",
                    importMode = mode,
                    names = ImmutableDictionary<string, string>.Empty.Add("copy_point", "Copy").Add("read_point", "Read")
                        .Add("point_size", "Size").Add("point_alignment", "Alignment").Add("by_value", "ByValue"),
                    externalTypes = [new() { nativeType = "Point", managedType = "System.Numerics.Vector2",
                        fields = ImmutableDictionary<string, string>.Empty.Add("x", "X").Add("y", "Y") }]
                },

                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.targetModels);
            var projection = Assert.Single(target.managed!.externalTypeProjections);
            Assert.Null(projection.managedStorage);
            Assert.Equal(8, projection.managedShape!.fields.Sum(field => field.size));
            Assert.Equal(16, target.interop.GetType(projection.nativeType).size);
            Assert.Equal(16, target.interop.GetType(projection.nativeType).alignment);
            Assert.Equal(0, target.interop.GetType(projection.nativeType).fields.Single(field => field.name == "x").offset);
            Assert.Equal(4, target.interop.GetType(projection.nativeType).fields.Single(field => field.name == "y").offset);
            Assert.DoesNotContain(target.interop.functions, function => function.exportName == "by_value");
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string factory = mode == BindingImportMode.FunctionTable ? "PointApi.Load(binary)" : "PointApi.Create()";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"),
                Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot))), configuration.library.nativeName, $$"""
                using BGCS.Runtime;
                using Generated.ExternalPolicyNative;
                public static unsafe class PointerShimConsumer {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        if(api.Raw.Size()!=sizeof(Point) || api.Raw.Size()!=16 || api.Raw.Alignment()!=16)return 1;
                        using var input=new NativeAlignedBuffer((nuint)api.Raw.Size(),(nuint)api.Raw.Alignment());
                        using var output=new NativeAlignedBuffer((nuint)api.Raw.Size(),(nuint)api.Raw.Alignment());
                        float* from=(float*)input.address;float* to=(float*)output.address;
                        from[0]=1;from[1]=2;
                        api.Raw.Copy((Point*)from,(Point*)to);
                        if(to[0]!=11 || to[1]!=22 || api.Raw.Read((Point*)to)!=33)return 2;
                        return 42;
                    }
                }
                """, "PointerShimConsumer", root, sourceRuntime: false));
            var previous = Snapshot(built.outputRoot);
            BindingConfiguration unsafeValue = configuration with
            {
                source = configuration.source with { selection = new() },
                bridge = configuration.bridge with { aggregateStrategy = AggregateCallStrategy.Direct }
            };
            GenerationResult refused = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(unsafeValue, root));
            Assert.False(refused.success);
            Assert.False(refused.published);
            Assert.Contains("calling-ABI evidence", refused.GetDiagnosticSummary(), StringComparison.Ordinal);
            Assert.Contains("pointer bridge", refused.GetDiagnosticSummary(), StringComparison.Ordinal);
            Assert.Equal(previous.OrderBy(pair => pair.Key), Snapshot(built.outputRoot).OrderBy(pair => pair.Key));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static System.Collections.Generic.Dictionary<string, string> Snapshot(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path),
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
