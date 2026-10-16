using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class RuntimeSourceFeatureConfigurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteSourceFeaturesReachPublicHostCompilationAndInvalidateTheMinimalEmissionCache(bool singleFile)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-runtime-complete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int NativeAdd(int a, int b);");
            BindingConfiguration configuration = Configuration() with
            {
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false },
                api = new() { @namespace = "Local.Generated", name = "NativeApi" },
                runtime = new() { mode = "source", @namespace = "Local.Generated.LocalRuntime" },
                nativeBuild = new() { mode = "none" },
                output = new() { root = "Generated", layout = singleFile ? "singleFile" : "split" },
                cache = new() { enabled = true, directory = "Cache" }
            };
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
            string build = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns()
                .WithWorkerAssetRoot(Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", build, "net10.0")).Build();
            BuildResult minimal = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(minimal.success, minimal.GetDiagnosticSummary());
            configuration = configuration with { runtime = configuration.runtime with { sourceFeatures = ["runtime.complete"] } };
            BuildResult complete = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(complete.success, complete.GetDiagnosticSummary());
            Assert.Contains(complete.generation.cacheStages, stage => stage.stage == "csharpEmission" && stage.status == "miss");
            string[] sourceFiles = Directory.GetFiles(complete.generation.outputRoot, "*.cs", SearchOption.AllDirectories);
            Assert.Contains(sourceFiles, path => File.ReadAllText(path).Contains("struct Atomic<T>", StringComparison.Ordinal));
            if (singleFile) Assert.EndsWith("Bindings.cs", Assert.Single(sourceFiles), StringComparison.Ordinal);
            BuildResult warm = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(warm.success, warm.GetDiagnosticSummary());
            Assert.Contains(warm.generation.cacheStages, stage => stage.stage == "csharpEmission" && stage.status == "hit");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FrozenConfigurationAndSchemaExposeOptionalCompleteSourceDelivery()
    {
        BindingConfiguration configuration = Configuration() with { runtime = new() { mode = "source", sourceFeatures = ["runtime.complete"] } };
        GenerationRequest request = new(configuration, Path.GetTempPath());
        Assert.Equal("runtime.complete", Assert.Single(request.configuration.runtime.sourceFeatures));
        Assert.NotNull(new SchemaCatalog().CreateBindingSchema()["properties"]!["runtime"]!["properties"]!["sourceFeatures"]);
        Assert.Empty(Configuration().runtime.sourceFeatures);
    }

    [Theory]
    [InlineData("package")]
    [InlineData("unknown")]
    [InlineData("case")]
    [InlineData("duplicate")]
    [InlineData("uninitialized")]
    public void InvalidSourceFeatureRequestsFailDuringFreeze(string fault)
    {
        RuntimeConfiguration runtime = new() { mode = "source", sourceFeatures = ["runtime.complete"] };
        runtime = fault switch
        {
            "package" => runtime with { mode = "package" },
            "unknown" => runtime with { sourceFeatures = ["missing.feature"] },
            "case" => runtime with { sourceFeatures = ["Runtime.Complete"] },
            "duplicate" => runtime with { sourceFeatures = ["runtime.complete", "runtime.complete"] },
            _ => runtime with { sourceFeatures = default }
        };
        Assert.Throws<ArgumentException>(() => new GenerationRequest(Configuration() with { runtime = runtime }, Path.GetTempPath()));
    }

    private static BindingConfiguration Configuration() => new() { schemaVersion = 1, library = new() { id = "source-complete", nativeName = "source_complete" }, source = new() { headers = ["native.h"] } };
}
