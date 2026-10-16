using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeMetadataTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task GeneratedAttributesRetainCompilerFactsAndRealCallsAcrossImportAndRuntimeModes(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-name-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                #if defined(_WIN32)
                #define FIXTURE_EXPORT __declspec(dllexport)
                #else
                #define FIXTURE_EXPORT __attribute__((visibility("default")))
                #endif
                FIXTURE_EXPORT int sample_add(int a, int b);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "native.c"), """
                #include "native.h"
                FIXTURE_EXPORT int sample_add(int a, int b) { return a * 3 + b; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "metadata", nativeName = "metadata_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.Metadata", name = "MetadataApi", importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    emitNativeMetadata = true, names = ImmutableDictionary<string, string>.Empty.Add("sample_add", "ManagedAdd")
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                nativeBuild = new() { mode = "sources", sources = ["native.c"] }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            var target = Assert.Single(result.generation.targetModels);
            Assert.True(target.managed!.emitNativeMetadata);
            Assert.Equal("sample_add", Assert.Single(target.interop.functions).exportName);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin")));
            string context = sourceRuntime ? "MetadataApiContext" : "MetadataApi";
            string runtime = sourceRuntime ? "Generated.Metadata.Runtime" : "BGCS.Runtime";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string privateImport = mode == BindingImportMode.FunctionTable ? "" : """
                var imports = typeof(ApiContext).GetNestedType("NativeImports", BindingFlags.NonPublic)!;
                var import = imports.GetMethod("Function0", BindingFlags.NonPublic | BindingFlags.Static)!;
                if (!Has(import, Names.NativeNameType.Func, "sample_add")) return 7;
                if (!Has(import.ReturnParameter, Names.NativeNameType.Type, "int")) return 8;
                """;
            string probe = $$"""
                using global::System;
                using global::System.Linq;
                using global::System.Reflection;
                using ApiContext = Generated.Metadata.{{context}};
                using Names = {{runtime}};
                public static class MetadataProbe {
                    private static bool Has(ICustomAttributeProvider provider, Names.NativeNameType kind, string value) =>
                        provider.GetCustomAttributes(typeof(Names.NativeNameAttribute),false).Cast<Names.NativeNameAttribute>()
                            .Count(attribute=>attribute.type==kind && attribute.name==value)==1;
                    public static int Run(string binary) {
                        var method=typeof(ApiContext.RawApi).GetMethod("ManagedAdd")!;
                        if(!Has(method,Names.NativeNameType.Func,"sample_add")) return 1;
                        if(!Has(method.ReturnParameter,Names.NativeNameType.Type,"int")) return 2;
                        var parameters=method.GetParameters();
                        if(!Has(parameters[0],Names.NativeNameType.Param,"a") || !Has(parameters[0],Names.NativeNameType.Type,"int")) return 3;
                        if(!Has(parameters[1],Names.NativeNameType.Param,"b") || !Has(parameters[1],Names.NativeNameType.Type,"int")) return 4;
                        if(typeof(ApiContext).GetMethod("ManagedAdd")!.GetCustomAttributes(typeof(Names.NativeNameAttribute),false).Length!=0) return 5;
                        {{privateImport}}
                        using var api={{factory}};
                        if(api.Raw.ManagedAdd(10,12)!=42 || api.ManagedAdd(4,5)!=17) return 6;
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary,
                configuration.library.nativeName, probe, "MetadataProbe", root, sourceRuntime));
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
