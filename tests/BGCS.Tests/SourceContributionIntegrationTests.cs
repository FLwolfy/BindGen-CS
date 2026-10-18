using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Tests.NeutralPlugin;
using Xunit;

namespace BGCS.Tests;

public sealed class SourceContributionIntegrationTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Add(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BridgeAdd(int value, out int result, out nint error);

    [Theory]
    [InlineData(SourceLanguage.C, "split")]
    [InlineData(SourceLanguage.Cpp, "singleFile")]
    public async Task ContractsOnlyManifestContributesSourcesThatSurviveStoreCleanupAndExternalFixtureCompilation(SourceLanguage language, string layout)
    {
        using Scratch scratch = new(language);
        string plugin = Path.Combine(scratch.root, "SourceFixturePlugin.dll");
        File.Copy(typeof(SourceFixturePlugin).Assembly.Location, plugin);
        byte[] content = await File.ReadAllBytesAsync(plugin);
        PluginManifest manifest = new()
        {
            schemaVersion = 1, assembly = Path.GetFileName(plugin), entryType = typeof(SourceFixturePlugin).FullName!,
            plugin = new SourceFixturePlugin().descriptor,
            assets = [new(Path.GetFileName(plugin), Digest(content))]
        };
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "plugin.json"), JsonSerializer.Serialize(manifest,
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }));
        BindingConfiguration configuration = scratch.Configuration() with
        {
            extensions = new() { plugins = ["plugin.json"] }, output = new() { root = "Generated", layout = layout }};
        await using BindingHost host = Builder().Build();
        GenerationRequest request = new(configuration, scratch.root);
        GenerationResult builtTogether = await NativeConsumerHarness.GenerateAndCompileAsync(host, request, compileManaged: true);
        GenerationResult cold = builtTogether;
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        var inspected = Assert.Single(cold.targetModels);
        var declaration = Assert.Single(inspected.native.declarations, item => item.name == "contributed_add");
        Assert.Contains("contributions/fixture.source/helpers/generated.h", declaration.source.file);
        Assert.DoesNotContain("source-inputs", declaration.source.file);
        string[] managed = cold.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp").Select(artifact => File.ReadAllText(Path.Combine(cold.outputRoot, artifact.relativePath))).ToArray();
        Assert.Contains("ContributedHelper", string.Join("\n", managed));
        if (layout == "singleFile") Assert.Single(managed);
        Assert.Contains(cold.artifacts, artifact => artifact.relativePath.EndsWith("/native/resources/contributions/fixture.source/helpers/payload.txt", StringComparison.Ordinal));
        string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(Path.Combine(cold.outputRoot, inspected.targetId))));
        nint native = NativeLibrary.Load(binary);
        try
        {
            if (language == SourceLanguage.C)
                Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<Add>(NativeLibrary.GetExport(native, "contributed_add"))(30));
            else
            {
                var export = Assert.Single(inspected.interop.functions, function => function.sourceDeclaration == declaration.id);
                Assert.Equal(0, Marshal.GetDelegateForFunctionPointer<BridgeAdd>(NativeLibrary.GetExport(native, export.exportName))(30, out int result, out nint error));
                Assert.Equal(42, result);
                Assert.Equal(0, error);
            }
        }
        finally { NativeLibrary.Free(native); }
        GenerationResult warm = await host.GenerateAsync(request);
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        Assert.Equal(declaration.id, Assert.Single(Assert.Single(warm.targetModels).native.declarations, item => item.name == "contributed_add").id);
        Assert.Equal("hit", Assert.Single(warm.cacheStages, stage => stage.stage == "csharpEmission").status);
    }

    [Fact]
    public async Task GeneratedBytesInvalidateCacheEvenWhenContributorFingerprintIsUnchanged()
    {
        using Scratch scratch = new(SourceLanguage.C);
        int version = 1, calls = 0, disposed = 0;
        await using BindingHost host = Builder().AddSourceContributor("mutable-source", 0,
            () => new MutableContributor(version, () => calls++, () => disposed++)).Build();
        GenerationRequest request = new(scratch.Configuration(), scratch.root);
        GenerationResult first = await host.GenerateAsync(request);
        Assert.True(first.success, first.GetDiagnosticSummary());
        GenerationResult warm = await host.GenerateAsync(request);
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        Assert.Equal("hit", Assert.Single(warm.cacheStages, stage => stage.stage == "csharpEmission").status);
        version = 2;
        GenerationResult changed = await host.GenerateAsync(request);
        Assert.True(changed.success, changed.GetDiagnosticSummary());
        Assert.Equal("miss", Assert.Single(changed.cacheStages, stage => stage.stage == "csharpEmission").status);
        Assert.Contains(Assert.Single(changed.targetModels).native.declarations, declaration => declaration.name == "generated_2");
        Assert.DoesNotContain(Assert.Single(changed.targetModels).native.declarations, declaration => declaration.name == "generated_1");
        Assert.Equal(3, calls);
        Assert.Equal(3, disposed);
    }

    [Theory]
    [InlineData("../escape.h")]
    [InlineData("bad\\header.h")]
    [InlineData("CON.h")]
    [InlineData("ambiguous/header.h.")]
    public async Task InvalidContributionCannotReplacePreviousCompleteOutput(string path)
    {
        using Scratch scratch = new(SourceLanguage.C);
        await using BindingHost initial = Builder().Build();
        GenerationRequest request = new(scratch.Configuration(), scratch.root);
        GenerationResult previous = await initial.GenerateAsync(request);
        Assert.True(previous.success, previous.GetDiagnosticSummary());
        byte[] report = await File.ReadAllBytesAsync(Path.Combine(previous.outputRoot, "generation.report.json"));
        await using BindingHost host = Builder().AddSourceContributor("invalid", 0, () => new DelegateContributor("invalid", (_, _) =>
            ValueTask.FromResult(new SourceContribution([SourceContributionArtifact.FromUtf8(path, SourceContributionRole.PublicHeader,
                "text/x-c-header", "int bad(void);", SourceLanguage.C, true)], new(EvidenceLevel.Declared, "invalid-path fixture"))))).Build();
        GenerationResult rejected = await host.GenerateAsync(request);
        Assert.False(rejected.success);
        Assert.Equal(report, await File.ReadAllBytesAsync(Path.Combine(previous.outputRoot, "generation.report.json")));
    }

    [Fact]
    public async Task UnfingerprintedContributorDisablesEmissionCacheAndCancellationReleasesSession()
    {
        using Scratch scratch = new(SourceLanguage.C);
        await using BindingHost host = Builder().AddSourceContributor("plain", 0, () => new DelegateContributor("plain", (_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new SourceContribution([SourceContributionArtifact.FromUtf8("plain.h", SourceContributionRole.PublicHeader,
                "text/x-c-header", "int plain(void);", SourceLanguage.C, true)], new(EvidenceLevel.Declared, "unfingerprinted source fixture")));
        })).Build();
        GenerationResult result = await host.GenerateAsync(new(scratch.Configuration(), scratch.root));
        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.Equal("disabled", Assert.Single(result.cacheStages, stage => stage.stage == "csharpEmission").status);
        using CancellationTokenSource cancellation = new();
        bool disposed = false;
        await using BindingHost cancelledHost = Builder().AddSourceContributor("cancelled", 0,
            () => new DelegateContributor("cancelled", (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); throw new InvalidOperationException(); }, () => disposed = true)).Build();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledHost.GenerateAsync(new(scratch.Configuration(), scratch.root), cancellation.Token));
        Assert.True(disposed);
    }


    private sealed class DelegateContributor(string name, Func<SourceContributionRequest, CancellationToken, ValueTask<SourceContribution>> create, Action? release = null) : ISourceContributor, IDisposable
    {
        public string id => name;
        public int priority => 0;
        public ValueTask<SourceContribution> ContributeAsync(SourceContributionRequest request, CancellationToken cancellationToken) => create(request, cancellationToken);
        public void Dispose() => release?.Invoke();
    }

    private sealed class MutableContributor(int version, Action called, Action released) : ISourceContributor, ICacheFingerprintProvider, IDisposable
    {
        public string id => "mutable-source";
        public int priority => 0;
        public ValueTask<SourceContribution> ContributeAsync(SourceContributionRequest request, CancellationToken cancellationToken)
        {
            called();
            return ValueTask.FromResult(new SourceContribution([SourceContributionArtifact.FromUtf8("generated.h", SourceContributionRole.PublicHeader,
                "text/x-c-header", "int generated_" + version + "(void);", SourceLanguage.C, true)], new(EvidenceLevel.Declared, "mutable fixture")));
        }
        public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken cancellationToken) => new(Digest(Encoding.UTF8.GetBytes("deliberately fixed private fingerprint")), []);
        public void Dispose() => released();
    }

    private static ContentDigest Digest(byte[] bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    private static BindingHostBuilder Builder()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0"));
    }

    private sealed class Scratch : IDisposable
    {
        private readonly SourceLanguage m_language;
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-source-contribution 验收 & " + Guid.NewGuid().ToString("N"));
        internal Scratch(SourceLanguage language)
        {
            m_language = language;
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "original.h"), "#pragma once\n");
        }
        internal BindingConfiguration Configuration() => new()
        {
            schemaVersion = 1, library = new() { id = "contributions", nativeName = "contributions_bridge" },
            source = new() { language = m_language, headers = ["original.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
            api = new() { @namespace = "Generated.Contributions", name = "ContributionsApi" }, runtime = new() { mode = "source" }
        };
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
