using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Tests;

public sealed class PrimaryFrontendCacheTests
{
    [Fact]
    public async Task VerifiedWorkerFactsRestoreAfterOutputDeletionAndDoNotSkipValidators()
    {
        using Scratch scratch = new();
        int validations = 0;
        await using BindingHost host = scratch.Builder().AddValidator("count", 0, () => new Validator(() => validations++)).Build();
        GenerationRequest request = scratch.Request();
        GenerationResult cold = await host.GenerateAsync(request);
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        Assert.Equal("miss", Primary(cold).status);
        string[] original = Bytes(cold);
        Assert.True(await host.ClearOwnedOutputAsync(request));
        GenerationResult warm = await host.GenerateAsync(request);
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        Assert.Equal("hit", Primary(warm).status);
        Assert.Equal("hit", Assert.Single(warm.cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        Assert.Equal(2, validations);
        Assert.Equal(original, Bytes(warm));
        JsonNode entry = JsonNode.Parse(File.ReadAllText(scratch.FrontendEntry()))!;
        entry["artifacts"]![0]!["content"] = Convert.ToBase64String([1, 2, 3]);
        File.WriteAllText(scratch.FrontendEntry(), entry.ToJsonString());
        GenerationResult repaired = await host.GenerateAsync(request);
        Assert.True(repaired.success, repaired.GetDiagnosticSummary());
        Assert.Equal("miss", Primary(repaired).status);
        Assert.Equal(original, Bytes(repaired));
        Assert.Equal(3, validations);
    }

    [Fact]
    public async Task ChangedHeaderBytesAndNewHigherPriorityHeaderInvalidateDetachedFacts()
    {
        using Scratch scratch = new();
        Directory.CreateDirectory(Path.Combine(scratch.source, "priority"));
        Directory.CreateDirectory(Path.Combine(scratch.source, "fallback"));
        string dependency = Path.Combine(scratch.source, "fallback", "value.h");
        File.WriteAllText(dependency, "typedef int native_value;\n");
        File.WriteAllText(Path.Combine(scratch.source, "native.h"), "#include <value.h>\nnative_value echo(native_value value);\n");
        GenerationRequest request = new(scratch.Request().configuration with
        { source = new() { headers = ["native.h"], parseMacros = false, includeDirectories = ["priority", "fallback"] } }, scratch.source);
        await using BindingHost host = scratch.Builder().Build();
        GenerationResult cold = await host.GenerateAsync(request);
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        GenerationResult warm = await host.GenerateAsync(request);
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        Assert.Equal("hit", Primary(warm).status);
        string[] warmBytes = Bytes(warm);
        File.WriteAllText(dependency, "typedef unsigned int native_value;\n");
        GenerationResult changed = await host.GenerateAsync(request);
        Assert.True(changed.success, changed.GetDiagnosticSummary());
        Assert.Equal("miss", Primary(changed).status);
        string[] changedBytes = Bytes(changed);
        Assert.NotEqual(warmBytes, changedBytes);
        File.WriteAllText(Path.Combine(scratch.source, "priority", "value.h"), "typedef long long native_value;\n");
        GenerationResult shadowed = await host.GenerateAsync(request);
        Assert.True(shadowed.success, shadowed.GetDiagnosticSummary());
        Assert.Equal("miss", Primary(shadowed).status);
        Assert.NotEqual(changedBytes, Bytes(shadowed));
    }

    [Fact]
    public async Task VolatilePreprocessingAlwaysParsesAndWorkerTamperCannotPublish()
    {
        using Scratch scratch = new();
        File.WriteAllText(Path.Combine(scratch.source, "native.h"), "#define BUILD_CLOCK __TIME__\nint echo(int value);\n");
        await using BindingHost host = scratch.Builder().Build();
        GenerationRequest request = scratch.Request();
        GenerationResult first = await host.GenerateAsync(request);
        Assert.True(first.success, first.GetDiagnosticSummary());
        Assert.Equal("disabled", Primary(first).status);
        Assert.Contains("Volatile", Primary(first).reason);
        GenerationResult second = await host.GenerateAsync(request);
        Assert.True(second.success, second.GetDiagnosticSummary());
        Assert.Equal("disabled", Primary(second).status);
        string formal = Path.Combine(second.outputRoot, "generation.report.json");
        byte[] before = File.ReadAllBytes(formal);
        File.AppendAllText(Path.Combine(scratch.worker, "BGCS.Frontend.Clang.Worker.runtimeconfig.json"), "tampered");
        GenerationResult failed = await host.GenerateAsync(request);
        Assert.False(failed.success);
        Assert.Equal(before, File.ReadAllBytes(formal));
    }

    private static GenerationCacheStageResult Primary(GenerationResult result) => Assert.Single(result.cacheStages.Where(stage => stage.stage == "primaryFrontend"));
    private static string[] Bytes(GenerationResult result) => result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
        .OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal)
        .Select(artifact => artifact.relativePath + ":" + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(result.outputRoot, artifact.relativePath)))).ToArray();

    private sealed class Validator(Action validate) : IGenerationValidator
    {
        public string id => "count";
        public int priority => 0;
        public ValueTask<ImmutableArray<BGCS.Contracts.BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        { validate(); return ValueTask.FromResult(ImmutableArray<BGCS.Contracts.BindingDiagnostic>.Empty); }
    }

    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-primary-cache-" + Guid.NewGuid().ToString("N"));
        internal string source => Path.Combine(root, "source");
        internal string worker => Path.Combine(root, "worker");
        internal Scratch()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
            string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
            string compiled = Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(worker);
            File.WriteAllText(Path.Combine(source, "native.h"), "int echo(int value);\n");
            foreach (string file in Directory.GetFiles(compiled)) File.Copy(file, Path.Combine(worker, Path.GetFileName(file)));
            string nativeRoot = Path.Combine(compiled, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
            string destination = Path.Combine(worker, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(nativeRoot)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            WorkerAssetManifest manifest = new(1, RuntimeInformation.RuntimeIdentifier, WorkerProtocol.C_PARSER_IDENTITY,
                Directory.GetFiles(worker, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(file =>
                {
                    using FileStream stream = File.OpenRead(file);
                    return new ArtifactDescriptor(Path.GetRelativePath(worker, file).Replace('\\', '/'), "application/octet-stream",
                        new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))));
                }).ToImmutableArray());
            File.WriteAllText(Path.Combine(worker, "worker-assets.manifest.json"), JsonSerializer.Serialize(manifest));
        }
        internal BindingHostBuilder Builder() => BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(worker);
        internal GenerationRequest Request() => new(new BindingConfiguration
        {
            schemaVersion = 1, library = new() { id = "primary_cache", nativeName = "primary_cache" },
            source = new() { headers = ["native.h"], parseMacros = false }, api = new() { @namespace = "Generated.PrimaryCache", name = "CacheApi" }
        }, source);
        internal string FrontendEntry() => Directory.GetFiles(Path.Combine(source, ".bgcs", "cache"), "*.stage.json")
            .Single(path => File.ReadAllText(path).Contains("frontend-result.json", StringComparison.Ordinal));
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
