using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Tests;

public sealed class WorkerPluginHostTests
{
    [Fact]
    public async Task FrozenWorkerStateParticipatesInBothCachesWithoutLoadingClangPluginIntoHost()
    {
        using WorkerPluginFixture fixture = new();
        await using BindingHost host = fixture.Builder().Build();
        GenerationRequest request = fixture.Request();
        GenerationResult cold = await host.GenerateAsync(request);
        AssertSuccess(cold, "first");
        Assert.Equal("miss", Stage(cold, "primaryFrontend"));
        Assert.Equal("miss", Stage(cold, "csharpEmission"));
        GenerationResult warm = await host.GenerateAsync(request);
        AssertSuccess(warm, "first");
        Assert.Equal("hit", Stage(warm, "primaryFrontend"));
        Assert.Equal("hit", Stage(warm, "csharpEmission"));
        File.WriteAllText(fixture.external, "second");
        GenerationResult changed = await host.GenerateAsync(request);
        AssertSuccess(changed, "second");
        Assert.Equal("miss", Stage(changed, "primaryFrontend"));
        Assert.Equal("miss", Stage(changed, "csharpEmission"));
        Assert.DoesNotContain(AssemblyLoadContext.Default.Assemblies, assembly => assembly.GetName().Name == "ClangFactPlugin");
    }

    [Fact]
    public async Task WorkerContributorsWithoutCompleteFingerprintsStillRunAndDisableBothCaches()
    {
        using WorkerPluginFixture fixture = new(fingerprinted: false);
        await using BindingHost host = fixture.Builder().Build();
        for (int run = 0; run < 2; run++)
        {
            GenerationResult result = await host.GenerateAsync(fixture.Request());
            AssertSuccess(result, "first");
            Assert.Equal("disabled", Stage(result, "primaryFrontend"));
            Assert.Equal("disabled", Stage(result, "csharpEmission"));
        }
    }

    [Fact]
    public async Task OriginalCppFactsAndGeneratedBridgeUseExplicitIndependentContributorPurposes()
    {
        using WorkerPluginFixture fixture = new();
        await using BindingHost host = fixture.Builder().Build();
        GenerationRequest request = fixture.Request();
        request = request.WithConfiguration(request.configuration with
        { source = request.configuration.source with { language = SourceLanguage.Cpp, languageStandard = null } });
        GenerationResult result = await host.GenerateAsync(request);
        AssertSuccess(result, "first");
        Assert.Contains(result.artifacts, artifact => artifact.relativePath.EndsWith("/native/include/bgcs_worker_host.h", StringComparison.Ordinal));
        Assert.Contains(result.artifacts, artifact => artifact.relativePath.EndsWith("/native/src/bgcs_worker_host.cpp", StringComparison.Ordinal));
        Assert.DoesNotContain(AssemblyLoadContext.Default.Assemblies, assembly => assembly.GetName().Name == "ClangFactPlugin");
    }

    [Fact]
    public async Task ComposedWorkerSettingsKeepEachValueOriginThroughCacheIdentityAndActivation()
    {
        using WorkerPluginFixture fixture = new();
        string child = Directory.CreateDirectory(Path.Combine(fixture.root, "child")).FullName;
        string basePath = Path.Combine(fixture.settings, "base.json");
        File.WriteAllText(basePath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, library = new { id = "worker_host", nativeName = "worker_host" },
            source = new { headers = new[] { Path.Combine(fixture.source, "native.h") }, parseMacros = false },
            output = new { root = fixture.output }, cache = new { directory = Path.Combine(fixture.root, "cache") },
            extensions = new { plugins = new[] { fixture.manifest }, options = new Dictionary<string, object>
                { ["clang.fixture"] = new { values = new { externalFile = "external.txt" } } } }
        }));
        string path = Path.Combine(child, "bindgen.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, extends = new[] { basePath },
            extensions = new { options = new Dictionary<string, object> { ["clang.fixture"] = new { values = new { mode = "normal" } } } }
        }));
        GenerationRequest request = GenerationRequest.FromFile(path);
        var configured = request.configuration.extensions.options["clang.fixture"];
        PluginConfiguration origins = new("clang.fixture", configured.values, configured.pathBase!, configured.valuePathBases);
        Assert.Equal(fixture.external, origins.ResolveInputPath("/externalFile"));
        Assert.Equal(child, origins.GetPathBase("/mode"));
        Assert.Throws<InvalidOperationException>(() => origins.pathBase);
        await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(fixture.worker).Build();
        GenerationResult cold = await host.GenerateAsync(request);
        AssertSuccess(cold, "first");
        GenerationResult warm = await host.GenerateAsync(request);
        AssertSuccess(warm, "first");
        Assert.Equal("hit", Stage(warm, "primaryFrontend"));
        Assert.Equal("hit", Stage(warm, "csharpEmission"));
        File.WriteAllText(fixture.external, "changed-inherited-input");
        GenerationResult changed = await host.GenerateAsync(request);
        AssertSuccess(changed, "changed-inherited-input");
        Assert.Equal("miss", Stage(changed, "primaryFrontend"));
        Assert.Equal("miss", Stage(changed, "csharpEmission"));
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("crash")]
    [InlineData("changeFingerprint")]
    public async Task WorkerFailurePreservesPreviousPublicationAndHostCanGenerateAgain(string mode)
    {
        using WorkerPluginFixture fixture = new();
        await using (BindingHost original = fixture.Builder().Build()) AssertSuccess(await original.GenerateAsync(fixture.Request()), "first");
        Dictionary<string, byte[]> before = Snapshot(fixture.output);
        int process = Environment.ProcessId;
        await using (BindingHost faulty = fixture.Builder(mode).Build())
        {
            GenerationResult failed = await faulty.GenerateAsync(fixture.Request());
            Assert.False(failed.success);
            Assert.Contains(failed.diagnostics, diagnostic => diagnostic.isFailure);
        }
        Assert.Equal(process, Environment.ProcessId);
        AssertSnapshot(fixture.output, before);
        await using BindingHost recovered = fixture.Builder().Build();
        AssertSuccess(await recovered.GenerateAsync(fixture.Request()), "first");
    }

    [Fact]
    public async Task DeclaredWorkerInputCannotChangeAfterCandidateValidationAndPublishStaleFacts()
    {
        using WorkerPluginFixture fixture = new();
        await using (BindingHost original = fixture.Builder().Build()) AssertSuccess(await original.GenerateAsync(fixture.Request()), "first");
        Dictionary<string, byte[]> before = Snapshot(fixture.output);
        await using BindingHost changed = fixture.Builder().AddValidator("late-input-change", 0, () => new LateMutation(fixture.external)).Build();
        GenerationResult failed = await changed.GenerateAsync(fixture.Request());
        Assert.False(failed.success);
        Assert.Contains(failed.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.Equal("changed-after-validation", File.ReadAllText(fixture.external));
        AssertSnapshot(fixture.output, before);
    }

    [Fact]
    public void WorkerParserIdentityAndServiceBoundaryAndSettingsAreValidatedWithoutActivation()
    {
        using WorkerPluginFixture fixture = new();
        Assert.ThrowsAny<Exception>(() => BindingHost.CreateBuilder().AddBuiltIns().AddPluginManifest(fixture.manifest,
            JsonSerializer.SerializeToElement(new { unexpected = true }), fixture.settings));
        PluginManifest original = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllBytes(fixture.manifest), WorkerProtocol.jsonOptions)!;
        fixture.WriteManifest(original with { workerParserIdentity = "different-parser" });
        Assert.ThrowsAny<Exception>(() => fixture.Builder());
        fixture.WriteManifest(original with { plugin = original.plugin with
        { services = [new("clang.fixture.facts", ExtensionPort.FrontendFactContributor), new("ordinary", ExtensionPort.InteropTransform)] } });
        Assert.ThrowsAny<Exception>(() => fixture.Builder());
        fixture.WriteManifest(original with { workerOnly = false, workerParserIdentity = null });
        Assert.ThrowsAny<Exception>(() => fixture.Builder());
        Assert.DoesNotContain(AssemblyLoadContext.Default.Assemblies, assembly => assembly.GetName().Name == "ClangFactPlugin");
    }

    private sealed class LateMutation(string path) : IGenerationValidator
    {
        public string id => "late-input-change";
        public int priority => 0;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        {
            File.WriteAllText(path, "changed-after-validation");
            return ValueTask.FromResult(ImmutableArray<BindingDiagnostic>.Empty);
        }
    }
    private static string Stage(GenerationResult result, string name) => Assert.Single(result.cacheStages, stage => stage.stage == name).status;
    private static void AssertSuccess(GenerationResult result, string text)
    {
        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.Equal("advanced-clang:read_value:" + text, Assert.Single(Assert.Single(result.targetModels).native.declarations,
            declaration => declaration.name == "read_value").documentation);
    }
    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal);
    private static void AssertSnapshot(string root, Dictionary<string, byte[]> before)
    {
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path)).Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, path)));
    }
}
