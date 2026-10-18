using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Plugins;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.Worker.Tests;

public sealed class WorkerFactPluginTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-worker-fact-plugin-" + Guid.NewGuid().ToString("N"));
    public WorkerFactPluginTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public async Task PreflightDoesNotParseAndLiveCursorContributionReturnsOnlyDetachedFacts()
    {
        FrontendRequest request = Create();
        await File.WriteAllTextAsync(request.headers[0], "#error preflight_must_not_parse\nint read_value(void);\n");
        FrontendWorkerClient client = Client();
        WorkerPluginCapture capture = await client.CapturePluginInputsAsync(request);
        Assert.Empty(capture.unfingerprintedServices);
        Assert.Equal(2, capture.fingerprints.Length);
        Assert.DoesNotContain(capture.inputs.files, file => file.path == request.headers[0]);
        Assert.Contains(capture.inputs.files, file => file.path == Path.Combine(m_root, "external.txt"));
        await File.WriteAllTextAsync(request.headers[0], "int read_value(void);\n");
        FrontendResult result = await client.ParseAsync(request with { workerPluginCapture = capture }, CancellationToken.None);
        Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.Equal("advanced-clang:read_value:first", Assert.Single(result.module.declarations, declaration => declaration.name == "read_value").documentation);
        Assert.NotNull(result.executionInputs);
        Assert.Contains(result.executionInputs.files, file => file.path == Path.Combine(m_root, "external.txt"));
        Assert.True(SourceSnapshotVerifier.Matches(result.executionInputs));
        Assert.DoesNotContain(AssemblyLoadContext.Default.Assemblies, assembly => assembly.GetName().Name == "ClangFactPlugin");
    }

    [Fact]
    public async Task MissingFingerprintsDisableAffectedReuseWithoutRejectingContribution()
    {
        FrontendRequest request = Create(fingerprinted: false);
        FrontendWorkerClient client = Client();
        WorkerPluginCapture capture = await client.CapturePluginInputsAsync(request);
        Assert.Equal(new[] { "plugin:clang.fixture", "service:clang.fixture.facts" }, capture.unfingerprintedServices);
        FrontendResult result = await client.ParseAsync(request with { workerPluginCapture = capture }, CancellationToken.None);
        Assert.Contains(result.module.declarations, declaration => declaration.documentation == "advanced-clang:read_value:first");
    }

    [Fact]
    public async Task ChangedDeclaredFileCannotReuseAnEarlierWorkerCapture()
    {
        FrontendRequest request = Create();
        FrontendWorkerClient client = Client();
        WorkerPluginCapture before = await client.CapturePluginInputsAsync(request);
        await File.WriteAllTextAsync(Path.Combine(m_root, "external.txt"), "second");
        await Assert.ThrowsAsync<InvalidDataException>(async () => await client.ParseAsync(request with { workerPluginCapture = before }, CancellationToken.None));
        WorkerPluginCapture after = await client.CapturePluginInputsAsync(request);
        Assert.NotEqual(before.fingerprints[0].state, after.fingerprints[0].state);
        FrontendResult result = await client.ParseAsync(request with { workerPluginCapture = after }, CancellationToken.None);
        Assert.Contains(result.module.declarations, declaration => declaration.documentation == "advanced-clang:read_value:second");
    }

    [Theory]
    [InlineData("throw", typeof(InvalidDataException))]
    [InlineData("changeFingerprint", typeof(InvalidDataException))]
    [InlineData("crash", typeof(IOException))]
    public async Task ContributorFailureOrDriftingStateStaysInsideWorker(string mode, Type exceptionType)
    {
        int host = Environment.ProcessId;
        FrontendRequest request = Create(mode: mode);
        FrontendWorkerClient client = Client();
        WorkerPluginCapture capture = await client.CapturePluginInputsAsync(request);
        await Assert.ThrowsAsync(exceptionType, async () => await client.ParseAsync(request with { workerPluginCapture = capture }, CancellationToken.None));
        Assert.Equal(host, Environment.ProcessId);
        FrontendResult ordinary = await client.ParseAsync(request with { workerPlugins = [], workerPluginCapture = null, workerPluginContext = null }, CancellationToken.None);
        Assert.Contains(ordinary.module.declarations, declaration => declaration.name == "read_value");
    }

    [Fact]
    public async Task ContributorTimeoutTerminatesOnlyWorker()
    {
        FrontendRequest request = Create(mode: "slow");
        WorkerPluginCapture capture = await Client().CapturePluginInputsAsync(request);
        int host = Environment.ProcessId;
        await Assert.ThrowsAsync<TimeoutException>(async () => await Client(TimeSpan.FromSeconds(3)).ParseAsync(request with { workerPluginCapture = capture }, CancellationToken.None));
        Assert.Equal(host, Environment.ProcessId);
    }

    [Fact]
    public async Task ContributorCancellationTerminatesItsWorkerAndHostCanContinue()
    {
        FrontendRequest request = Create(mode: "slow");
        WorkerPluginCapture capture = await Client().CapturePluginInputsAsync(request);
        using CancellationTokenSource cancellation = new();
        Task<FrontendResult> parse = Client().ParseAsync(request with { workerPluginCapture = capture }, cancellation.Token).AsTask();
        string marker = Path.Combine(m_root, "external.txt.worker-pid");
        try
        {
            using CancellationTokenSource startup = new(TimeSpan.FromSeconds(30));
            while (!File.Exists(marker)) await Task.Delay(20, startup.Token);
            int worker = int.Parse(await File.ReadAllTextAsync(marker));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parse);
            try
            {
                using Process process = Process.GetProcessById(worker);
                Assert.True(process.HasExited, "The advanced contributor worker survived cancellation.");
            }
            catch (ArgumentException) { }
            FrontendResult ordinary = await Client().ParseAsync(request with { workerPlugins = [], workerPluginCapture = null, workerPluginContext = null }, CancellationToken.None);
            Assert.Contains(ordinary.module.declarations, declaration => declaration.name == "read_value");
        }
        finally
        {
            cancellation.Cancel();
            try { await parse.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DiscoveryContextCannotUseADifferentNativeTarget(bool changeIdentity)
    {
        FrontendRequest request = Create();
        NativeTargetDescriptor target = request.target;
        NativeTargetDescriptor different = new(changeIdentity ? new(target.targetId.value + "-other") : target.targetId,
            target.platformId, target.architectureId, target.abiId, changeIdentity ? target.triple : target.triple + "-other",
            target.toolchain, target.capabilities, target.byteOrder);
        request = request with { workerPluginContext = new(request.workerPluginContext!.configuration, m_root, different) };
        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(async () => await Client().CapturePluginInputsAsync(request));
        Assert.Contains("target identity", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PinnedParserIdentityIsRequiredBeforePluginActivation()
    {
        FrontendRequest request = Create();
        request = request with { workerPlugins = request.workerPlugins.SetItem(0, request.workerPlugins[0] with { parserIdentity = "unrelated-parser" }) };
        await Assert.ThrowsAsync<InvalidDataException>(async () => await Client().CapturePluginInputsAsync(request));
    }

    [Theory]
    [InlineData(FrontendRequestPurpose.BridgeVerification)]
    public async Task VerificationPurposeIsExplicitAndStillTracksWorkerInputs(FrontendRequestPurpose purpose)
    {
        FrontendRequest request = Create() with { purpose = purpose };
        await File.WriteAllTextAsync(request.headers[0], "int verification_only(void);\n");
        FrontendWorkerClient client = Client();
        WorkerPluginCapture capture = await client.CapturePluginInputsAsync(request);
        FrontendResult result = await client.ParseAsync(request with { workerPluginCapture = capture }, CancellationToken.None);
        Assert.Contains(result.module.declarations, declaration => declaration.name == "verification_only");
        Assert.DoesNotContain(result.module.declarations, declaration => declaration.documentation?.StartsWith("advanced-clang:", StringComparison.Ordinal) == true);
        Assert.NotNull(result.executionInputs);
        Assert.Contains(result.executionInputs.files, file => file.path == Path.Combine(m_root, "external.txt"));
        Assert.Equal(2, capture.fingerprints.Length);
    }

    [Fact]
    public async Task UnknownFrontendPurposeIsRejectedBeforeActivation()
    {
        FrontendRequest request = Create() with { purpose = (FrontendRequestPurpose)999 };
        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(async () => await Client().CapturePluginInputsAsync(request));
        Assert.Contains("purpose", failure.Message, StringComparison.Ordinal);
    }

    private FrontendRequest Create(bool fingerprinted = true, string mode = "normal")
    {
        string solution = Root();
        string output = Path.Combine(solution, "tests", "fixtures", "plugins", "ClangFactPlugin", "bin", Configuration(), "net10.0");
        string[] names = ["ClangFactPlugin.dll", "ClangFactPlugin.deps.json"];
        foreach (string name in names) File.Copy(Path.Combine(output, name), Path.Combine(m_root, name));
        File.WriteAllText(Path.Combine(m_root, "input.h"), "int read_value(void);\n");
        File.WriteAllText(Path.Combine(m_root, "external.txt"), "first");
        PluginDescriptor descriptor = new("clang.fixture", "1.0.0", [new("clang.fixture.facts", ExtensionPort.FrontendFactContributor)])
        {
            configurationSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object", additionalProperties = false, required = new[] { "externalFile" },
                properties = new { externalFile = new { type = "string", minLength = 1 }, mode = new { type = "string" } }
            })
        };
        PluginManifest manifest = new()
        {
            schemaVersion = 1, assembly = "ClangFactPlugin.dll", entryType = fingerprinted ? "ClangFactFixture.FingerprintedFactPlugin" : "ClangFactFixture.FactPlugin",
            plugin = descriptor, workerOnly = true, workerParserIdentity = WorkerProtocol.C_PARSER_IDENTITY,
            assets = names.Select(name => new PluginAsset(name, new(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(m_root, name))))))).ToImmutableArray()
        };
        string manifestPath = Path.Combine(m_root, "plugin.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }));
        LockedPluginManifest locked = new PluginManifestReader().Read(manifestPath);
        WorkerPluginActivation activation = new(locked.path, locked.manifest, locked.inputs, locked.missingInputs,
            JsonSerializer.SerializeToElement(new { externalFile = "external.txt", mode }), m_root, ImmutableDictionary<string, string>.Empty, WorkerProtocol.C_PARSER_IDENTITY);
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
        return new(target, SourceLanguage.C, [Path.Combine(m_root, "input.h")])
        {
            pathBase = m_root, parseMacros = false, workerPlugins = [activation],
            workerPluginContext = new(JsonSerializer.SerializeToElement(new { source = "fixture" }), m_root, target)
        };
    }

    private static FrontendWorkerClient Client(TimeSpan? timeout = null)
        => new(Path.Combine(Root(), "src", "BGCS.Frontend.Clang.Worker", "bin", Configuration(), "net10.0", "BGCS.Frontend.Clang.Worker.dll"), timeout ?? TimeSpan.FromSeconds(60));
    private static string Configuration() => AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
    private static string Root()
    {
        DirectoryInfo? path = new(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "BindGen-CS.slnx"))) path = path.Parent;
        return path!.FullName;
    }
    public void Dispose() { if (Directory.Exists(m_root)) Directory.Delete(m_root, recursive: true); }
}
