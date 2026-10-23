using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Workers;
using BGCS.Core.Plugins;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using BGCS.Core;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Hosting;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Infrastructure.Workers;
using BGCS.Infrastructure.Caching;
using BGCS.Hosting.Standard;
using Xunit;

namespace BGCS.Tests;

public sealed class PrimaryFrontendCacheTests
{
    [Fact]
    public async Task ExplicitSdkRootsRejectUnlistedImplicitHeadersAndPreserveAdditiveInputsAndWarmReuse()
    {
        using Scratch scratch = new();
        string sdk = Path.Combine(scratch.root, "selected-sdk"), selected = Path.Combine(sdk, "selected");
        string implicitHeaders = Path.Combine(sdk, "usr", "include"), additive = Path.Combine(scratch.root, "source-extra");
        foreach (string directory in new[] { selected, implicitHeaders, additive }) Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(selected, "selected.h"), "typedef int bgcs_selected;\n");
        File.WriteAllText(Path.Combine(additive, "source_extra.h"), "typedef int bgcs_extra;\n");
        File.WriteAllText(Path.Combine(implicitHeaders, "bgcs_unlisted.h"), "typedef int bgcs_unlisted;\n");
        // This locked builtin leaf has no dynamic include query that would
        // intentionally disable primary caching independently of SDK authority.
        File.WriteAllText(Path.Combine(scratch.source, "native.h"), "#include <__stddef_size_t.h>\n#include <selected.h>\n#include <source_extra.h>\nsize_t measured_value(size_t value);\nbgcs_selected selected_value(bgcs_extra value);\n");
        GenerationRequest request = new(scratch.Request().configuration with
        {
            source = new() { headers = ["native.h"], parseMacros = false, systemIncludeDirectories = [additive] },
            targets = [new() { id = "linux-x64-gnu", settings = new() { sysRoot = sdk, systemIncludeDirectories = [selected] } }]
        }, scratch.source);
        await using BindingHost host = scratch.Builder().Build();
        GenerationResult cold = await host.GenerateAsync(request);
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        GenerationResult warm = await host.GenerateAsync(request);
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        Assert.True(Primary(warm).status == "hit", Primary(warm).reason);
        Assert.Equal("hit", Assert.Single(warm.cacheStages, stage => stage.stage == "csharpEmission").status);
        Assert.Contains(Assert.Single(warm.targetModels).native.declarations, declaration => declaration.name == "selected_value");
        var before = Directory.GetFiles(warm.outputRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(warm.outputRoot, path), File.ReadAllBytes);
        File.AppendAllText(Path.Combine(scratch.source, "native.h"), "#include <bgcs_unlisted.h>\nbgcs_unlisted unlisted_value(void);\n");
        GenerationResult rejected = await host.GenerateAsync(request);
        Assert.False(rejected.success, rejected.GetDiagnosticSummary());
        Assert.False(rejected.published);
        Assert.Contains("bgcs_unlisted.h", rejected.GetDiagnosticSummary());
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(warm.outputRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(warm.outputRoot, path)).Order());
        foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(Path.Combine(warm.outputRoot, entry.Key)));
    }

    [Fact]
    public async Task VerifiedWorkerFactsRestoreAfterOutputDeletionAndDoNotSkipValidators()
    {
        using Scratch scratch = new();
        int validations = 0;
        await using BindingHost host = scratch.Builder().RegisterFactory<IGenerationValidator>(new("count", ExtensionPort.GenerationValidator, 0), _ => new Validator(() => validations++)).Build();
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
    public async Task AContentVerifiedCacheEntryWithAnInvalidNativeGraphIsReparsed()
    {
        using Scratch scratch = new();
        await using BindingHost host = scratch.Builder().Build();
        GenerationRequest request = scratch.Request();
        GenerationResult cold = await host.GenerateAsync(request);
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        string[] original = Bytes(cold);
        JsonNode stored = JsonNode.Parse(File.ReadAllText(scratch.FrontendEntry()))!;
        byte[] content = Convert.FromBase64String(stored["artifacts"]![0]!["content"]!.GetValue<string>());
        FrontendResult parsed = JsonSerializer.Deserialize<FrontendResult>(content, WorkerProtocol.jsonOptions)!;
        NativeModule module = parsed.module;
        Assert.NotEmpty(module.types);
        NativeModule invalid = new(module.targetId, module.triple, module.pointerSize,
            module.types.SetItem(0, module.types[0] with { elementType = new StableId("fixture:missing-native-type") }), module.declarations, module.diagnostics)
        { language = module.language, byteOrder = module.byteOrder };
        byte[] malformed = JsonSerializer.SerializeToUtf8Bytes(parsed with { module = invalid }, WorkerProtocol.jsonOptions);
        ContentDigest key = new(stored["key"]!.GetValue<string>());
        ArtifactDescriptor descriptor = new("frontend-result.json", "application/json", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(malformed))));
        using HostServices cacheHost = new HostServicesProvider().Create(new(scratch.source, JsonSerializer.SerializeToElement(new { })));
        GenerationStageCache cache = new(Path.Combine(scratch.source, ".bgcs", "cache"), cacheHost.paths);
        Assert.True(cache.Store(key, [new CachedStageArtifact(descriptor, malformed)]));
        Assert.True(cache.TryRead(key, out ImmutableArray<CachedStageArtifact> verified, out string? miss));
        Assert.Null(miss);
        Assert.Equal(malformed, Assert.Single(verified).content.ToArray());

        GenerationResult repaired = await host.GenerateAsync(request);
        Assert.True(repaired.success, repaired.GetDiagnosticSummary());
        Assert.Equal("miss", Primary(repaired).status);
        Assert.Contains("native graph", Primary(repaired).reason!, StringComparison.Ordinal);
        Assert.Equal(original, Bytes(repaired));
        GenerationResult warm = await host.GenerateAsync(request);
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        Assert.Equal("hit", Primary(warm).status);
    }

    [Fact]
    public async Task AProcessorChangingTheHeaderAfterWarmAdmissionCannotReplaceThePublication()
    {
        using Scratch scratch = new();
        string header = Path.Combine(scratch.source, "native.h");
        string original = File.ReadAllText(header);
        bool armed = false;
        int processed = 0;
        await using BindingHost host = scratch.Builder().RegisterFactory<IArtifactProcessor>(
            new("fixture.late-native-mutation", ExtensionPort.ArtifactProcessor), _ => new MutatingProcessor(() =>
            {
                processed++;
                if (armed) File.WriteAllText(header, "double echo(double value);\n");
            })).Build();
        GenerationRequest request = scratch.Request();
        GenerationResult cold = await host.GenerateAsync(request);
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        GenerationResult warm = await host.GenerateAsync(request);
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        Assert.Equal("hit", Primary(warm).status);
        Dictionary<string, string> before = Publication(warm.outputRoot);
        armed = true;
        GenerationResult rejected = await host.GenerateAsync(request);
        Assert.False(rejected.success);
        Assert.False(rejected.published);
        Assert.Equal("hit", Primary(rejected).status);
        Assert.Contains("Input file bytes changed", rejected.GetDiagnosticSummary(), StringComparison.Ordinal);
        Assert.Equal("double echo(double value);\n", File.ReadAllText(header));
        Assert.Equal(before.OrderBy(item => item.Key), Publication(warm.outputRoot).OrderBy(item => item.Key));
        Assert.Equal(3, processed);

        armed = false;
        File.WriteAllText(header, original);
        GenerationResult recovered = await host.GenerateAsync(request);
        Assert.True(recovered.success, recovered.GetDiagnosticSummary());
        Assert.Equal("hit", Primary(recovered).status);
        Assert.Equal(4, processed);

        static Dictionary<string, string> Publication(string output)
            => Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(
                file => Path.GetRelativePath(output, file), file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), StringComparer.Ordinal);
    }

    [Fact]
    public async Task VolatilePreprocessingAlwaysParsesAndWorkerTamperCannotPublish()
    {
        using Scratch scratch = new();
        File.WriteAllText(Path.Combine(scratch.source, "native.h"), "#define BUILD_CLOCK __TIME__\nint echo(int value);\n");
        ConcurrentQueue<SourceLanguage> parses = new();
        await using BindingHost host = scratch.Builder().ObserveWorkerParses(scratch.worker, parses).Build();
        GenerationRequest request = scratch.Request();
        GenerationResult first = await host.GenerateAsync(request);
        Assert.True(first.success, first.GetDiagnosticSummary());
        Assert.Equal("disabled", Primary(first).status);
        Assert.Contains("Volatile", Primary(first).reason);
        Assert.Equal("miss", Assert.Single(first.cacheStages, stage => stage.stage == "csharpEmission").status);
        string[] original = Bytes(first);
        GenerationResult second = await host.GenerateAsync(request);
        Assert.True(second.success, second.GetDiagnosticSummary());
        Assert.Equal("disabled", Primary(second).status);
        Assert.Equal("hit", Assert.Single(second.cacheStages, stage => stage.stage == "csharpEmission").status);
        Assert.Equal(original, Bytes(second));
        Assert.Equal(new[] { SourceLanguage.C, SourceLanguage.C }, parses.ToArray());
        File.WriteAllText(Path.Combine(scratch.source, "native.h"), "#define BUILD_CLOCK __TIME__\ndouble echo(double value);\n");
        GenerationResult changed = await host.GenerateAsync(request);
        Assert.True(changed.success, changed.GetDiagnosticSummary());
        Assert.Equal("disabled", Primary(changed).status);
        Assert.Equal("miss", Assert.Single(changed.cacheStages, stage => stage.stage == "csharpEmission").status);
        Assert.NotEqual(original, Bytes(changed));
        Assert.Equal(new[] { SourceLanguage.C, SourceLanguage.C, SourceLanguage.C }, parses.ToArray());
        string formal = Path.Combine(changed.outputRoot, "generation.report.json");
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
        public ValueTask<ImmutableArray<BGCS.Core.BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        { validate(); return ValueTask.FromResult(ImmutableArray<BGCS.Core.BindingDiagnostic>.Empty); }
    }

    private sealed class MutatingProcessor(Action process) : IArtifactProcessor, ICacheFingerprintProvider
    {
        public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken cancellationToken)
            => new(new ContentDigest(new string('d', 64)), []);
        public ValueTask ProcessAsync(ArtifactProcessingRequest request, CancellationToken cancellationToken)
        {
            // Deliberately violate the processor's declared-input contract to
            // prove the independent native-input publication fence still holds.
            process();
            return ValueTask.CompletedTask;
        }
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
            string compiled = Path.Combine(repository!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(worker);
            File.WriteAllText(Path.Combine(source, "native.h"), "int echo(int value);\n");
            foreach (string file in Directory.GetFiles(compiled)) File.Copy(file, Path.Combine(worker, Path.GetFileName(file)));
            string nativeRoot = Path.Combine(compiled, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
            string destination = Path.Combine(worker, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(nativeRoot)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            WorkerAssetManifest manifest = new(1, RuntimeInformation.RuntimeIdentifier, ClangParserIdentity.C_IDENTITY,
                Directory.GetFiles(worker, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(file =>
                {
                    using FileStream stream = File.OpenRead(file);
                    return new ArtifactDescriptor(Path.GetRelativePath(worker, file).Replace('\\', '/'), "application/octet-stream",
                        new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))));
                }).ToImmutableArray());
            File.WriteAllText(Path.Combine(worker, "worker-assets.manifest.json"), JsonSerializer.Serialize(manifest));
        }
        internal BindingHostBuilder Builder() => BindingGenerator.CreateBuilder().UseWorkerDirectory(worker);
        internal GenerationRequest Request() => new(new BindingConfiguration
        {
            schemaVersion = 2, library = new() { id = "primary_cache", nativeName = "primary_cache" },
            source = new() { headers = ["native.h"], parseMacros = false }, api = new() { @namespace = "Generated.PrimaryCache", name = "CacheApi" }
        }, source);
        internal string FrontendEntry() => Directory.GetFiles(Path.Combine(source, ".bgcs", "cache"), "*.stage.json")
            .Single(path => File.ReadAllText(path).Contains("frontend-result.json", StringComparison.Ordinal));
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
