using System;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class FlatIdentifierNamingTests
{
    [Fact]
    public async Task VocabularyChangesInvalidateWarmEmissionWithoutChangingNativeExports()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-flat-naming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int CREATEWINDOW(void); int DEPTHBUFFER(void); int GETMOUSEPOS(void);");
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
            string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns()
                .WithWorkerAssetRoot(Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0")).Build();
            BindingConfiguration config = new()
            {
                schemaVersion = 1,
                library = new() { id = "flat_names", nativeName = "flat_names" }, source = new() { headers = ["native.h"], parseMacros = false },
                api = new() { @namespace = "Generated.FlatNames", name = "FlatApi", naming = new()
                    { flatIdentifierSegmentation = ManagedFlatIdentifierSegmentation.English, functions = new() { casing = ManagedNameCasing.Pascal } } }
            };
            GenerationResult first = await host.GenerateAsync(new(config, root));
            Assert.True(first.success, first.GetDiagnosticSummary());
            string source = Read(first);
            Assert.Contains("CreateWindow(", source);
            Assert.Contains("DepthBuffer(", source);
            Assert.Contains("GetMousePos(", source);
            Assert.Equal(new[] { "CREATEWINDOW", "DEPTHBUFFER", "GETMOUSEPOS" }, Assert.Single(first.targetModels).interop.functions.Select(function => function.exportName).Order(StringComparer.Ordinal));
            GenerationResult warm = await host.GenerateAsync(new(config, root));
            Assert.True(warm.success, warm.GetDiagnosticSummary());
            Assert.Equal("hit", Assert.Single(warm.cacheStages, stage => stage.stage == "csharpEmission").status);
            config = config with { api = config.api with { naming = config.api.naming with
                { flatIdentifierSegmentation = ManagedFlatIdentifierSegmentation.Custom, flatIdentifierWords = ["createwindow", "depthbuffer", "getmousepos"] } } };
            GenerationResult changed = await host.GenerateAsync(new(config, root));
            Assert.True(changed.success, changed.GetDiagnosticSummary());
            Assert.Equal("miss", Assert.Single(changed.cacheStages, stage => stage.stage == "csharpEmission").status);
            Assert.Contains("Createwindow(", Read(changed));
            config = config with { api = config.api with { naming = config.api.naming with { flatIdentifierWords = ["create", "window", "depthbuffer", "getmousepos"] } } };
            GenerationResult edited = await host.GenerateAsync(new(config, root));
            Assert.True(edited.success, edited.GetDiagnosticSummary());
            Assert.Equal("miss", Assert.Single(edited.cacheStages, stage => stage.stage == "csharpEmission").status);
            Assert.Contains("CreateWindow(", Read(edited));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GroupedJsonAndSchemaExposeOnlyTheImmutableVocabularyChoices()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-flat-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "bindgen.json");
            File.WriteAllText(path, """
                {"schemaVersion":1,"library":{"id":"names","nativeName":"names"},"source":{"headers":["native.h"]},
                 "api":{"naming":{"flatIdentifierSegmentation":"custom","flatIdentifierWords":["create","window"],"functions":{"casing":"pascal"}}}}
                """);
            ManagedNamingPolicy policy = GenerationRequest.FromFile(path).configuration.api.naming;
            Assert.Equal(ManagedFlatIdentifierSegmentation.Custom, policy.flatIdentifierSegmentation);
            Assert.Equal(new[] { "create", "window" }, policy.flatIdentifierWords.ToArray());
            string schema = new SchemaCatalog().CreateBindingSchema().ToJsonString();
            Assert.Contains("flatIdentifierSegmentation", schema);
            Assert.Contains("flatIdentifierWords", schema);
            Assert.Equal(ManagedFlatIdentifierSegmentation.None, new ManagedNamingPolicy().flatIdentifierSegmentation);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string Read(GenerationResult result) => string.Join("\n", result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
        .Select(artifact => File.ReadAllText(Path.Combine(result.outputRoot, artifact.relativePath))));
}
