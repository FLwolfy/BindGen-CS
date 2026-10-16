using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Targets;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using BGCS.Platforms;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeExternalProjectionTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ExistingNumericsAndReorderedClosedGenericTypesCopyThroughTheVerifiedRawAbi(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-external-projection-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "point.h"), """
                #if defined(_WIN32)
                #define EXPORT __declspec(dllexport)
                #else
                #define EXPORT __attribute__((visibility("default")))
                #endif
                typedef struct Point { float x; float y; } Point;
                typedef struct GenericPoint { float x; float y; } GenericPoint;
                EXPORT Point transform(Point value);
                EXPORT GenericPoint transform_generic(GenericPoint value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "point.c"), """
                #include "point.h"
                EXPORT Point transform(Point value) { Point result = {value.x + 10, value.y + 20}; return result; }
                EXPORT GenericPoint transform_generic(GenericPoint value) { GenericPoint result = {value.x + 10, value.y + 20}; return result; }
                """);
            const string consumerTypes = """
                namespace ExternalConsumer {
                    public struct Pair<T> where T : unmanaged { public T Y; public T X; }
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(root, "consumer-types.cs"), consumerTypes);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "external", nativeName = "point_fixture" },
                source = new() { headers = ["point.h"], includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.External", name = "PointApi", importMode = mode,
                    names = ImmutableDictionary<string, string>.Empty.Add("Point::x", "nativeX").Add("transform_generic", "TransformGeneric"),
                    externalTypes =
                    [
                        new() { nativeType = "Point", managedType = "System.Numerics.Vector2", fields = Fields() },
                        new() { nativeType = "GenericPoint", managedType = "ExternalConsumer.Pair<float>", fields = Fields() }
                    ]
                },
                managedBuild = new() { sources = ["consumer-types.cs"] },
                nativeBuild = new() { mode = "sources", sources = ["point.c"] },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.generation.targetModels);
            Assert.Equal(2, target.managed!.externalTypeProjections.Length);
            var generic = target.managed.externalTypeProjections.Single(item => item.managedType.Contains("Pair", StringComparison.Ordinal));
            Assert.Equal(4, generic.managedStorage!.fields.Single(field => field.name == "X").offset);
            Assert.Equal(0, target.interop.GetType(generic.nativeType).fields.Single(field => field.name == "x").offset);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin")));
            CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(target.managed, new(configuration.library.nativeName, new NoWrites())
            { importMode = mode, runtimeNamespace = sourceRuntime ? "Generated.External.Runtime" : "BGCS.Runtime", includeRuntimeSource = sourceRuntime,
                managedRuntime = ManagedRuntimeResolver.Resolve(target.resolvedTarget!, ManagedRuntimeKind.CoreClr) });
            Assert.Equal(2, plan.externalTypeProjections.Length);
            Assert.DoesNotContain(plan.methods, method => method.parameterTypes.Any(type => type.Contains("Vector2", StringComparison.Ordinal) || type.Contains("Pair<", StringComparison.Ordinal)));
            var sources = Directory.GetFiles(Path.Combine(targetRoot, "managed"), "*.cs", SearchOption.AllDirectories)
                .Select(path => (Path: path, Text: File.ReadAllText(path))).ToArray();
            Assert.Contains(sources, source => source.Text.Contains("result.nativeX = value.X;", StringComparison.Ordinal));
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                sources.Select(source => (source.Path, source.Text.Replace("result.nativeX = value.X;", "result.nativeX = value.Y;", StringComparison.Ordinal))), configuration.library.nativeName));
            string factory = mode == BindingImportMode.FunctionTable ? "PointApi.Load(binary)" : "PointApi.Create()";
            string consumer = $$"""
                using Generated.External;
                using System.Numerics;
                public static class ExternalProbe {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        Vector2 result = api.Transform(new Vector2(1, 2));
                        var generic = api.TransformGeneric(new ExternalConsumer.Pair<float> { X = 3, Y = 4 });
                        if (result.X != 11 || result.Y != 22 || generic.X != 13 || generic.Y != 24) return 1;
                        return 42;
                    }
                }
                {{consumerTypes}}
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary,
                configuration.library.nativeName, consumer, "ExternalProbe", root, sourceRuntime));
            if (mode == BindingImportMode.FunctionTable && !sourceRuntime)
            {
                var previous = Directory.GetFiles(result.outputRoot, "*", SearchOption.AllDirectories)
                    .ToDictionary(path => Path.GetRelativePath(result.outputRoot, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);
                BindingConfiguration incompatible = configuration with { api = configuration.api with
                {
                    externalTypes = [configuration.api.externalTypes[0], configuration.api.externalTypes[1] with { managedType = "ExternalConsumer.Pair<int>" }]
                } };
                BuildResult rejected = await host.BuildAsync(new(new(incompatible, root)));
                Assert.False(rejected.success);
                Assert.Contains("scalar kind, signedness and width", rejected.GetDiagnosticSummary(), StringComparison.Ordinal);
                var after = Directory.GetFiles(result.outputRoot, "*", SearchOption.AllDirectories)
                    .ToDictionary(path => Path.GetRelativePath(result.outputRoot, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);
                Assert.Equal(previous.OrderBy(pair => pair.Key), after.OrderBy(pair => pair.Key));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ImmutableDictionary<string, string> Fields() => ImmutableDictionary<string, string>.Empty.Add("x", "X").Add("y", "Y");
    private sealed class NoWrites : IArtifactSink
    {
        public string WriteText(string relativePath, string content) => throw new InvalidOperationException("Planning cannot write artifacts.");
    }
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
