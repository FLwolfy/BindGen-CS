using BGCS.Core.Plugins;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Managed;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class CacheExtensionRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewlyRegisteredContributionCannotReuseAFormerHostsGenerationResult(bool backend)
    {
        using Workspace workspace = new();
        GenerationRequest request = new(new BindingConfiguration
        {
            schemaVersion = 2,
            library = new() { id = "extension_cache", nativeName = "extension_cache" },
            source = new() { headers = ["native.h"], parseMacros = false },
            api = new() { @namespace = "Generated.ExtensionCache", name = "ExtensionApi" }
        }, workspace.root);
        await using (BindingHost first = Builder().Build())
        {
            GenerationResult cold = await first.GenerateAsync(request);
            Assert.True(cold.success, cold.GetDiagnosticSummary());
            Assert.Equal("miss", EmissionCache(cold));
            GenerationResult warm = await first.GenerateAsync(request);
            Assert.True(warm.success, warm.GetDiagnosticSummary());
            Assert.Equal("hit", EmissionCache(warm));
        }
        int calls = 0;
        BindingHostBuilder builder = Builder();
        if (backend) builder.RegisterFactory<IBindingBackend>(new("fixture.backend", ExtensionPort.BindingBackend), _ => new MarkerBackend(() => calls++));
        else builder.RegisterFactory<IArtifactProcessor>(new("new-processor", ExtensionPort.ArtifactProcessor, 0), _ => new MarkerProcessor(() => calls++));
        await using BindingHost changed = builder.Build();
        GenerationResult result = await changed.GenerateAsync(request);
        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.Equal(1, calls);
        Assert.Equal("disabled", EmissionCache(result));
        ArtifactDescriptor marker = Assert.Single(result.artifacts, artifact => artifact.relativePath.EndsWith("contribution.marker", StringComparison.Ordinal));
        Assert.Equal("executed", await File.ReadAllTextAsync(Path.Combine(result.outputRoot, marker.relativePath)));
        Assert.Equal(Digest("executed"), marker.digest);
    }

    private static string EmissionCache(GenerationResult result)
        => Assert.Single(result.cacheStages, stage => stage.stage == "csharpEmission").status;

    private sealed class MarkerProcessor(Action called) : IArtifactProcessor
    {
        public string id => "new-processor";
        public int priority => 0;
        public async ValueTask ProcessAsync(
            ArtifactProcessingRequest request,
            CancellationToken cancellationToken
        ) {
            called();
            await request.content.AddTextAsync(request.api.interop.targetId + "/managed/contribution.marker", "text/plain", "executed", cancellationToken);
        }
    }

    private sealed class MarkerBackend(Action called) : IBindingBackend
    {
        public async ValueTask<ImmutableArray<ArtifactDescriptor>> EmitAsync(
            ManagedApiModel model,
            EmissionRequest request,
            CancellationToken cancellationToken
        ) {
            called();
            ImmutableArray<ArtifactDescriptor> artifacts = await new CSharpBackend().EmitAsync(model, request, cancellationToken);
            request.sink.WriteText("contribution.marker", "executed");
            return artifacts.Add(new("contribution.marker", "text/plain", Digest("executed")));
        }
    }

    private static ContentDigest Digest(string content) => new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))));

    private static BindingHostBuilder Builder()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return BindingGenerator.CreateBuilder().UseWorkerDirectory(Path.Combine(repository.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0"));
    }

    private sealed class Workspace : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-neutral-cache-extension-" + Guid.NewGuid().ToString("N"));
        internal Workspace()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "native.h"), "int add(int first, int second);");
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
