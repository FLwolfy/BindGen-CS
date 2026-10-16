using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using Xunit;

namespace BGCS.Tests;

public sealed class RemoteConfigurationTests
{
    [Fact]
    public async Task PinnedBasesPreserveLocalPathOriginsProfilesAndRequestProvenance()
    {
        using Scratch scratch = new();
        string contentRoot = Path.Combine(scratch.root, "content");
        Directory.CreateDirectory(contentRoot);
        byte[] body = Encoding.UTF8.GetBytes("""
            {"schemaVersion":1,"library":{"id":"remote","nativeName":"remote"},
             "source":{"headers":["native.h"],"includeDirectories":["include"]},
             "api":{"namespace":"Remote.Base"},"cache":{"enabled":true},
             "profiles":{"dev":{"api":{"name":"DevApi"},"output":{"root":"profile-output"}}}}
            """);
        using Transport transport = new((_, _) => Task.FromResult(Reply(body)));
        using HttpClient client = new(transport);
        BindingConfigurationLoader loader = new(client);
        string path = scratch.WriteRoot(body, new JsonObject { ["profile"] = "dev", ["cache"] = new JsonObject { ["enabled"] = false },
            ["api"] = new JsonObject { ["namespace"] = "Remote.Current" } });
        GenerationRequest request = await GenerationRequest.FromFileAsync(path, configurationLoader: loader);
        Assert.Equal("Remote.Current", request.configuration.api.@namespace);
        Assert.Equal("DevApi", request.configuration.api.name);
        Assert.False(request.configuration.cache.enabled);
        Assert.Equal(Path.Combine(contentRoot, "native.h"), Assert.Single(request.configuration.source.headers));
        Assert.Equal(Path.Combine(contentRoot, "include"), Assert.Single(request.configuration.source.includeDirectories));
        Assert.Equal(Path.Combine(contentRoot, "profile-output"), request.configuration.output.root);
        Assert.Single(request.configurationInputs);
        PinnedConfigurationInput input = Assert.Single(request.configurationRemoteInputs);
        Assert.Equal("https://configs.example/base.json", input.url.AbsoluteUri);
        Assert.Equal(Pin(body), input.digest.value);
        Assert.Equal(body.Length, input.length);
        Assert.Equal(contentRoot, input.pathBase);
        Assert.Equal(request.configurationRemoteInputs, request.WithConfiguration(request.configuration).configurationRemoteInputs);
        Assert.Single(transport.addresses);
        Assert.Empty(Directory.GetFiles(contentRoot));
        // The caller retains the transport; completing a load must not dispose it.
        using HttpResponseMessage stillUsable = await client.GetAsync("https://configs.example/other.json");
        Assert.Equal(HttpStatusCode.OK, stillUsable.StatusCode);
    }

    [Fact]
    public async Task WorkspaceAsyncLoadingRetainsEveryPinnedProjectInput()
    {
        using Scratch scratch = new();
        byte[] body = Encoding.UTF8.GetBytes(Base);
        using Transport transport = new((_, _) => Task.FromResult(Reply(body)));
        using HttpClient client = new(transport);
        scratch.WriteRoot(body);
        string workspace = Path.Combine(scratch.root, "workspace.json");
        await File.WriteAllTextAsync(workspace, """
            {"schemaVersion":1,"projects":[{"id":"one","configuration":"bindgen.json"}]}
            """);
        BindingWorkspace loaded = await new WorkspaceLoader().LoadAsync(workspace, configurationLoader: new(client));
        GenerationRequest request = Assert.Single(loaded.projects).request;
        Assert.Single(request.configurationRemoteInputs);
        Assert.Equal(2, request.configurationInputs.Length);
    }

    [Fact]
    public async Task GenerationPublishesPinnedProvenanceAndAChangedPinChangesTheActualApi()
    {
        using Scratch scratch = new();
        string contentRoot = Path.Combine(scratch.root, "content");
        Directory.CreateDirectory(contentRoot);
        await File.WriteAllTextAsync(Path.Combine(contentRoot, "native.h"), "int add(int left, int right);");
        byte[] body = Encoding.UTF8.GetBytes(Base);
        using Transport transport = new((_, _) => Task.FromResult(Reply(body)));
        using HttpClient client = new(transport);
        BindingConfigurationLoader loader = new(client);
        string path = scratch.WriteRoot(body);
        GenerationRequest request = await GenerationRequest.FromFileAsync(path, configurationLoader: loader);
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(
            Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0")).Build();
        GenerationResult first = await host.GenerateAsync(request);
        Assert.True(first.success, first.GetDiagnosticSummary());
        GenerationResult repeated = await host.GenerateAsync(request);
        Assert.True(repeated.success, repeated.GetDiagnosticSummary());
        Assert.Single(transport.addresses);
        string receipt = Assert.Single(repeated.artifacts.Where(artifact => artifact.relativePath.EndsWith("/verification.json", StringComparison.Ordinal))).relativePath;
        using (JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(repeated.outputRoot, receipt))))
        {
            JsonElement remote = Assert.Single(document.RootElement.GetProperty("configurationRemoteInputs").EnumerateArray());
            Assert.Equal(Pin(body), remote.GetProperty("digest").GetProperty("value").GetString());
            Assert.Equal("https://configs.example/base.json", remote.GetProperty("url").GetString());
            Assert.Single(document.RootElement.GetProperty("configurationInputs").EnumerateArray());
        }
        body = Encoding.UTF8.GetBytes(Base[..^1] + ",\"api\":{\"name\":\"ChangedPinnedApi\"}}");
        scratch.WriteRoot(body);
        GenerationRequest changed = await GenerationRequest.FromFileAsync(path, configurationLoader: loader);
        GenerationResult generated = await host.GenerateAsync(changed);
        Assert.True(generated.success, generated.GetDiagnosticSummary());
        Assert.Equal(2, transport.addresses.Count);
        string source = string.Join("\n", generated.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
            .Select(artifact => File.ReadAllText(Path.Combine(generated.outputRoot, artifact.relativePath))));
        Assert.Contains("partial class ChangedPinnedApi", source);
        using JsonDocument finalReceipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(generated.outputRoot, receipt)));
        Assert.Equal(Pin(body), Assert.Single(finalReceipt.RootElement.GetProperty("configurationRemoteInputs").EnumerateArray()).GetProperty("digest").GetProperty("value").GetString());
    }

    [Fact]
    public async Task HashMismatchIsRejectedBeforeRemoteContentCanAffectConfiguration()
    {
        using Scratch scratch = new();
        byte[] expected = Encoding.UTF8.GetBytes(Base);
        byte[] different = Encoding.UTF8.GetBytes(Base.Replace("remote", "changed", StringComparison.Ordinal));
        using Transport transport = new((_, _) => Task.FromResult(Reply(different)));
        using HttpClient client = new(transport);
        IOException failure = await Assert.ThrowsAsync<IOException>(() => new BindingConfigurationLoader(client).LoadWithInputsAsync(scratch.WriteRoot(expected)));
        Assert.Contains("SHA-256", failure.Message);
        Assert.Equal(new[] { "bindgen.json" }, Directory.GetFiles(scratch.root).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("http://configs.example/base.json")]
    [InlineData("https://configs.example/base.json#untracked")]
    [InlineData("https://name:password@configs.example/base.json")]
    public async Task InvalidRemoteOriginsFailWithoutSendingAnyRequest(string address)
    {
        using Scratch scratch = new();
        byte[] body = Encoding.UTF8.GetBytes(Base);
        using Transport transport = new((_, _) => throw new InvalidOperationException("No request should be sent."));
        using HttpClient client = new(transport);
        string path = scratch.WriteRoot(body, url: address);
        await Assert.ThrowsAsync<JsonException>(() => new BindingConfigurationLoader(client).LoadWithInputsAsync(path));
        Assert.Empty(transport.addresses);
    }

    [Theory]
    [InlineData("sha256")]
    [InlineData("pathBase")]
    [InlineData("unknown")]
    [InlineData("plain-url")]
    [InlineData("url-path-base")]
    public async Task UnpinnedOrAmbiguousReferencesNeverFallBackToTheWorkingDirectory(string fault)
    {
        using Scratch scratch = new();
        byte[] body = Encoding.UTF8.GetBytes(Base);
        string path = scratch.WriteRoot(body);
        JsonObject root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        JsonObject reference = root["extends"]![0]!.AsObject();
        if (fault is "sha256" or "pathBase") reference.Remove(fault);
        else if (fault == "unknown") reference["extra"] = true;
        else if (fault == "url-path-base") reference["pathBase"] = "https://configs.example/headers/";
        else root["extends"]![0] = "https://configs.example/base.json";
        await File.WriteAllTextAsync(path, root.ToJsonString());
        using Transport transport = new((_, _) => throw new InvalidOperationException("No request should be sent."));
        using HttpClient client = new(transport);
        if (fault == "plain-url") await Assert.ThrowsAsync<ArgumentException>(() => new BindingConfigurationLoader(client).LoadWithInputsAsync(path));
        else await Assert.ThrowsAsync<JsonException>(() => new BindingConfigurationLoader(client).LoadWithInputsAsync(path));
        Assert.Empty(transport.addresses);
    }

    [Fact]
    public async Task RedirectsRemainHttpsAndCannotEscapeTheirBoundOrContentPin()
    {
        using Scratch scratch = new();
        byte[] body = Encoding.UTF8.GetBytes(Base);
        string path = scratch.WriteRoot(body);
        using Transport valid = new((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/base.json"
            ? Redirect("/final.json") : Reply(body)));
        using HttpClient validClient = new(valid);
        ConfigurationLoadResult accepted = await new BindingConfigurationLoader(validClient).LoadWithInputsAsync(path);
        Assert.Equal(2, valid.addresses.Count);
        Assert.Equal(Pin(body), Assert.Single(accepted.remoteInputs).digest.value);
        using Transport insecure = new((_, _) => Task.FromResult(Redirect("http://configs.example/plain.json")));
        using HttpClient insecureClient = new(insecure);
        await Assert.ThrowsAsync<IOException>(() => new BindingConfigurationLoader(insecureClient).LoadWithInputsAsync(path));
        Assert.Single(insecure.addresses);
        using Transport loop = new((_, _) => Task.FromResult(Redirect("/again.json")));
        using HttpClient loopClient = new(loop);
        await Assert.ThrowsAsync<IOException>(() => new BindingConfigurationLoader(loopClient).LoadWithInputsAsync(path));
        Assert.Equal(6, loop.addresses.Count);
    }

    [Fact]
    public async Task CancellationReachesTheActualConfigurationTransport()
    {
        using Scratch scratch = new();
        byte[] body = Encoding.UTF8.GetBytes(Base);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using Transport transport = new(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("An unbounded transport must have been cancelled.");
        });
        using HttpClient client = new(transport);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
        Task<ConfigurationLoadResult> pending = new BindingConfigurationLoader(client).LoadWithInputsAsync(scratch.WriteRoot(body), cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Single(transport.addresses);
    }

    [Fact]
    public async Task ChunkedInputCannotBypassTheResponseByteLimitOrWriteAnUnboundedCache()
    {
        using Scratch scratch = new();
        using Transport transport = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(new RepeatedStream(16 * 1024 * 1024 + 1)) }));
        using HttpClient client = new(transport);
        IOException failure = await Assert.ThrowsAsync<IOException>(() =>
            new BindingConfigurationLoader(client).LoadWithInputsAsync(scratch.WriteRoot(Encoding.UTF8.GetBytes(Base))));
        Assert.Contains("16 MiB", failure.Message);
        Assert.Equal(new[] { "bindgen.json" }, Directory.GetFiles(scratch.root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ARemoteDocumentParticipatesInLocalInheritanceCycleDetection()
    {
        using Scratch scratch = new();
        byte[] body = Encoding.UTF8.GetBytes("""{"schemaVersion":1,"extends":["../bindgen.json"]}""");
        using Transport transport = new((_, _) => Task.FromResult(Reply(body)));
        using HttpClient client = new(transport);
        ArgumentException failure = await Assert.ThrowsAsync<ArgumentException>(() => new BindingConfigurationLoader(client).LoadWithInputsAsync(scratch.WriteRoot(body)));
        Assert.Contains("cycle", failure.Message);
        Assert.Single(transport.addresses);
    }

    [Fact]
    public void EditorSchemaRequiresTheSameExplicitRemoteIdentityAndPathBase()
    {
        JsonObject schema = new SchemaCatalog().CreateBindingSchema();
        JsonObject remote = schema["properties"]!["extends"]!["items"]!["oneOf"]![1]!.AsObject();
        Assert.Equal(new[] { "url", "sha256", "pathBase" }, remote["required"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.False(remote["additionalProperties"]!.GetValue<bool>());
    }

    private const string Base = """{"schemaVersion":1,"library":{"id":"remote","nativeName":"remote"},"source":{"headers":["native.h"]}}""";
    private static string Pin(byte[] body) => Convert.ToHexStringLower(SHA256.HashData(body));
    private static HttpResponseMessage Reply(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    private static HttpResponseMessage Redirect(string address)
    {
        HttpResponseMessage response = new(HttpStatusCode.Redirect);
        response.Headers.Location = new(address, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        internal readonly List<Uri> addresses = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { addresses.Add(request.RequestUri!); return handler(request, cancellationToken); }
    }

    private sealed class RepeatedStream(int length) : Stream
    {
        private int m_remaining = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = Math.Min(count, m_remaining);
            buffer.AsSpan(offset, read).Fill((byte)' ');
            m_remaining -= read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = Math.Min(buffer.Length, m_remaining);
            buffer.Span[..read].Fill((byte)' ');
            m_remaining -= read;
            return ValueTask.FromResult(read);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Scratch : IDisposable
    {
        internal readonly string root = Path.Combine(Path.GetTempPath(), "bgcs remote configuration " + Guid.NewGuid().ToString("N"));
        internal Scratch() => Directory.CreateDirectory(root);
        internal string WriteRoot(byte[] body, JsonObject? overrides = null, string url = "https://configs.example/base.json")
        {
            JsonObject document = overrides ?? new();
            document["schemaVersion"] = 1;
            document["extends"] = new JsonArray(new JsonObject { ["url"] = url, ["sha256"] = Pin(body), ["pathBase"] = "content" });
            string path = Path.Combine(root, "bindgen.json");
            File.WriteAllText(path, document.ToJsonString());
            return path;
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
