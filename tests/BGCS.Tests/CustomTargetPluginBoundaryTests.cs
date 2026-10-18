using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms.Targeting;
using Xunit;
using ExamplePlugin = BGCS.Examples.CustomTargetPlugin.CustomTargetPlugin;

namespace BGCS.Tests;

public sealed class CustomTargetPluginBoundaryTests
{
    [Fact]
    public async Task ContractsOnlyTargetUsesRealWorkerAndInvalidatesProfileAndSdkInputsBeforeWarmReuse()
    {
        using Fixture fixture = new();
        bool mutate = false;
        await using BindingHost host = fixture.Builder().AddValidator("private-target-late-change", 0,
            () => new MutationValidator(() => { if (mutate) fixture.WriteHeader(44); })).Build();
        GenerationRequest request = GenerationRequest.FromFile(fixture.configuration);
        GenerationResult first = await host.GenerateAsync(request);
        Assert.True(first.success, first.GetDiagnosticSummary());
        Assert.Equal("example-private-host", Assert.Single(first.targetModels).native.targetId);
        Assert.Contains(Assert.Single(first.targetModels).native.declarations, value => value.name == "ExampleSdkLimit" && value.evaluatedIntegralValue == "42");
        GenerationResult warm = await host.GenerateAsync(request);
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        Assert.Equal("hit", Assert.Single(warm.cacheStages, stage => stage.stage == "primaryFrontend").status);
        Assert.Equal("hit", Assert.Single(warm.cacheStages, stage => stage.stage == "csharpEmission").status);
        fixture.WriteHeader(43);
        GenerationResult sdkChanged = await host.GenerateAsync(request);
        Assert.True(sdkChanged.success, sdkChanged.GetDiagnosticSummary());
        Assert.Contains(Assert.Single(sdkChanged.targetModels).native.declarations, value => value.name == "ExampleSdkLimit" && value.evaluatedIntegralValue == "43");
        Assert.NotEqual("hit", Assert.Single(sdkChanged.cacheStages, stage => stage.stage == "primaryFrontend").status);
        fixture.WriteProfile(2);
        GenerationResult profileChanged = await host.GenerateAsync(request);
        Assert.True(profileChanged.success, profileChanged.GetDiagnosticSummary());
        Assert.Contains(Assert.Single(profileChanged.targetModels).native.declarations, value => value.name == "ExampleProfile" && value.evaluatedIntegralValue == "2");
        Assert.NotEqual("hit", Assert.Single(profileChanged.cacheStages, stage => stage.stage == "primaryFrontend").status);
        Dictionary<string, byte[]> before = Directory.GetFiles(profileChanged.outputRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(profileChanged.outputRoot, path), File.ReadAllBytes);
        mutate = true;
        GenerationResult rejected = await host.GenerateAsync(request);
        Assert.False(rejected.success);
        Assert.False(rejected.published);
        Assert.Contains("changed", rejected.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(profileChanged.outputRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(profileChanged.outputRoot, path)).Order());
        foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(profileChanged.outputRoot, path)));
    }

    [Fact]
    public async Task UnknownPlatformNeedsAnExplicitExistingRuntimeAndCannotSelectBrowserAbi()
    {
        using Fixture fixture = new();
        await using BindingHost host = fixture.Builder().Build();
        GenerationRequest request = GenerationRequest.FromFile(fixture.configuration);
        GenerationResult missing = await host.GenerateAsync(new(request.configuration with
            { api = request.configuration.api with { runtime = null } }, fixture.root));
        Assert.False(missing.success);
        Assert.False(missing.published);
        Assert.Contains("api.runtime", missing.GetDiagnosticSummary());
        GenerationResult browser = await host.GenerateAsync(new(request.configuration with
            { api = request.configuration.api with { runtime = ManagedRuntimeKind.BrowserWasm } }, fixture.root));
        Assert.False(browser.success);
        Assert.False(browser.published);
        Assert.Contains("Wasm", browser.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(missing.outputRoot));
    }

    [Fact]
    public void RemovedToolchainAliasIsRejectedWhileExplicitSdkSettingsRemainAccepted()
    {
        using Fixture fixture = new();
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(fixture.configuration))!;
        json["targets"]![0]!["toolchain"] = "auto";
        string removed = Path.Combine(fixture.root, "removed.json");
        File.WriteAllText(removed, json.ToJsonString());
        Assert.Throws<JsonException>(() => GenerationRequest.FromFile(removed));
        json["targets"]![0]!.AsObject().Remove("toolchain");
        json["targets"]![0]!["settings"] = new System.Text.Json.Nodes.JsonObject
        { ["systemIncludeDirectories"] = new System.Text.Json.Nodes.JsonArray("sdk/include") };
        File.WriteAllText(removed, json.ToJsonString());
        string declared = Assert.Single(GenerationRequest.FromFile(removed).configuration.targets).settings.systemIncludeDirectories.Single();
        Assert.Equal(Path.Combine(fixture.root, "sdk", "include"), Path.GetFullPath(declared, fixture.root));
    }

    private sealed class MutationValidator(Action mutate) : IGenerationValidator, ICacheFingerprintProvider
    {
        public string id => "private-target-late-change";
        public int priority => 0;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        { mutate(); return ValueTask.FromResult(ImmutableArray<BindingDiagnostic>.Empty); }
        public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken cancellationToken)
            => new(new(Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("private-target-late-input-check:1")))), []);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string root = Path.Combine(Path.GetTempPath(), "bgcs-private-target-" + Guid.NewGuid().ToString("N"));
        internal readonly string configuration;
        private readonly string m_repository;
        private string? m_worker;
        internal Fixture()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
            m_repository = repository!.FullName;
            Directory.CreateDirectory(Path.Combine(root, "sdk", "include"));
            string example = Path.Combine(m_repository, "examples", "CustomTargetPlugin");
            File.Copy(Path.Combine(example, "native.h"), Path.Combine(root, "native.h"));
            configuration = Path.Combine(root, "bindgen.json");
            File.Copy(Path.Combine(example, "bindgen.json"), configuration);
            byte[] plugin = File.ReadAllBytes(typeof(ExamplePlugin).Assembly.Location);
            File.WriteAllBytes(Path.Combine(root, "target-plugin.dll"), plugin);
            PluginManifest manifest = new() { schemaVersion = 1, assembly = "target-plugin.dll", entryType = typeof(ExamplePlugin).FullName!,
                plugin = new ExamplePlugin().descriptor, assets = [new("target-plugin.dll", new(Convert.ToHexStringLower(SHA256.HashData(plugin))))] };
            File.WriteAllText(Path.Combine(root, "plugin.json"), JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }));
            WriteProfile(1);
            WriteHeader(42);
        }
        internal BindingHostBuilder Builder()
            => BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(m_worker ??= DeployWorker());

        private string DeployWorker()
        {
            string build = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
            string compiled = Path.Combine(m_repository, "src", "BGCS.Frontend.Clang.Worker", "bin", build, "net10.0");
            string worker = Path.Combine(root, "worker");
            Directory.CreateDirectory(worker);
            foreach (string file in Directory.GetFiles(compiled))
            {
                if (Path.GetFileName(file) is "worker-assets.manifest.json" or ".bgcs-artifacts.json") continue;
                File.Copy(file, Path.Combine(worker, Path.GetFileName(file)));
            }
            string native = Path.Combine("runtimes", RuntimeInformation.RuntimeIdentifier, "native");
            Directory.CreateDirectory(Path.Combine(worker, native));
            foreach (string file in Directory.GetFiles(Path.Combine(compiled, native)))
                File.Copy(file, Path.Combine(worker, native, Path.GetFileName(file)));
            WorkerAssetManifest assets = new(1, RuntimeInformation.RuntimeIdentifier, WorkerProtocol.C_PARSER_IDENTITY,
                Directory.GetFiles(worker, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                    .Select(file => new ArtifactDescriptor(Path.GetRelativePath(worker, file).Replace('\\', '/'), "application/octet-stream",
                        new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))))).ToImmutableArray());
            File.WriteAllText(Path.Combine(worker, "worker-assets.manifest.json"), JsonSerializer.Serialize(assets));
            return worker;
        }
        internal void WriteHeader(int value) => File.WriteAllText(Path.Combine(root, "sdk", "include", "private_sdk.h"), "#define EXAMPLE_SDK_VALUE " + value + "\n");
        internal void WriteProfile(int revision)
        {
            NativeTargetDescriptor host = new TargetResolver().Resolve(new(new("host")));
            File.WriteAllText(Path.Combine(root, "target-sdk.json"), JsonSerializer.Serialize(new
            {
                platformId = "private-desktop", host.architectureId, host.abiId, host.triple,
                byteOrder = "littleEndian", requiresExplicitSdk = true, supportsDynamicModules = true,
                systemIncludeDirectories = new[] { "sdk/include" }, defines = new[] { "PROFILE_VERSION=" + revision }
            }));
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
