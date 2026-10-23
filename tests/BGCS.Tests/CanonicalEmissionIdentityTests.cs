using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class CanonicalEmissionIdentityTests
{
    [Theory]
    [InlineData("{\"z\":1,\"a\":{\"third\":3,\"first\":1}}", "{\"a\":{\"first\":1,\"third\":3},\"z\":1}")]
    [InlineData("{\"unicode\":\"λ😀\",\"array\":[{\"b\":2,\"a\":1},true,null]}", "{\"array\":[{\"a\":1,\"b\":2},true,null],\"unicode\":\"λ😀\"}")]
    [InlineData("{\"items\":[{\"id\":null,\"x\":false,\"zero\":0},{\"id\":\"λ😀\",\"nested\":{\"id\":2}},{\"new\":1,\"id\":null}]}", "{\"items\":[{\"zero\":0,\"x\":false,\"id\":null},{\"nested\":{\"id\":2},\"id\":\"λ😀\"},{\"id\":null,\"new\":1}]}")]
    [InlineData("{\"\\u0061\":1,\"long name <>\":null,\"array\":[{\"\\u0062\":false,\"a\":0},{\"b\":false,\"a\":1}]}", "{\"array\":[{\"a\":0,\"b\":false},{\"a\":1,\"b\":false}],\"long name <>\":null,\"a\":1}")]
    [InlineData("""{"λ😀":1,"\u0001\t\"\\":2,"<>&":{"λ😀":false,"\u0001\t\"\\":null}}""",
        """{"<>&":{"\u0001\t\"\\":null,"\u03bb\ud83d\ude00":false},"\u0001\t\"\\":2,"\u03bb\ud83d\ude00":1}""")]
    public async Task EquivalentObjectOrderingReusesTheVerifiedEmissionAndRestoresExactBytes(
        string firstSettings,
        string reorderedSettings
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-canonical-emission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int add(int left, int right);");
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).AddPlugin<SettingsPlugin>().Build();
            BindingConfiguration firstConfiguration = Configuration(firstSettings);
            GenerationResult first = await host.GenerateAsync(new(firstConfiguration, root));
            Assert.True(first.success, first.GetDiagnosticSummary());
            Assert.Equal("miss", EmissionStatus(first));
            string[] bytes = ManagedBytes(first);
            Assert.True(await host.ClearOwnedOutputAsync(new(firstConfiguration, root)));
            GenerationResult reordered = await host.GenerateAsync(new(Configuration(reorderedSettings), root));
            Assert.True(reordered.success, reordered.GetDiagnosticSummary());
            Assert.Equal("hit", EmissionStatus(reordered));
            Assert.Equal(bytes, ManagedBytes(reordered));
            GenerationResult changed = await host.GenerateAsync(new(Configuration("{\"changed\":true}"), root));
            Assert.True(changed.success, changed.GetDiagnosticSummary());
            Assert.Equal("miss", EmissionStatus(changed));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("{\"flag\":null}")]
    [InlineData("{\"flag\":false}")]
    [InlineData("{\"flag\":0}")]
    public async Task ExplicitNullFalseAndZeroSettingsRemainDistinctFromAnAbsentProperty(string settings)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-canonical-value-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int add(int left, int right);");
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).AddPlugin<SettingsPlugin>().Build();
            GenerationResult absent = await host.GenerateAsync(new(Configuration("{}"), root));
            Assert.True(absent.success, absent.GetDiagnosticSummary());
            Assert.Equal("miss", EmissionStatus(absent));
            GenerationResult present = await host.GenerateAsync(new(Configuration(settings), root));
            Assert.True(present.success, present.GetDiagnosticSummary());
            Assert.Equal("miss", EmissionStatus(present));
            GenerationResult repeated = await host.GenerateAsync(new(Configuration(settings), root));
            Assert.True(repeated.success, repeated.GetDiagnosticSummary());
            Assert.Equal("hit", EmissionStatus(repeated));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("""{"λ😀":1,"\u0001\t\"\\":2,"<>&":{"λ😀":false,"\u0001\t\"\\":null}}""")]
    [InlineData("""{"\u03bb\ud83d\ude00":[{"<>&":0,"quote\"slash\\":true},{"<>&":null,"quote\"slash\\":false}],"line\nreturn\r":{}}""")]
    [InlineData("""{"ascii":"short","nested":{"ascii":"printable <>&","values":["plain","quote\"","slash\\","line\nreturn\r","λ😀","\u03bb\ud83d\ude00"]}}""")]
    public async Task PublishedPropertyNamesPreserveTheReferenceWriterBytes(string settings)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-json-property-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int add(int left, int right);");
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).AddPlugin<SettingsPlugin>().Build();
            GenerationResult result = await host.GenerateAsync(new(Configuration(settings), root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            using JsonDocument metadata = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(result.outputRoot,
                Assert.Single(result.targetModels).targetId, "metadata", "verification.json")));
            JsonElement published = Assert.Single(metadata.RootElement.GetProperty("plugins").EnumerateArray(),
                plugin => plugin.GetProperty("descriptor").GetProperty("id").GetString() == "canonical.settings").GetProperty("settings");

            // Match the public document's nesting while independently encoding
            // every decoded name through the standard writer, without pooling.
            using MemoryStream bytes = new();
            using (Utf8JsonWriter writer = new(bytes, new() { Indented = true, MaxDepth = 64 }))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("plugins");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WritePropertyName("settings");
                JsonSerializer.Deserialize<JsonElement>(settings).WriteTo(writer);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            using JsonDocument reference = JsonDocument.Parse(bytes.ToArray());
            JsonElement expected = Assert.Single(reference.RootElement.GetProperty("plugins").EnumerateArray()).GetProperty("settings");
            Assert.Equal(Encoding.UTF8.GetBytes(expected.GetRawText()), Encoding.UTF8.GetBytes(published.GetRawText()));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BindingConfiguration Configuration(string settings) => new()
    {
        schemaVersion = 2,
        library = new() { id = "canonical", nativeName = "canonical" },
        source = new() { headers = ["native.h"], parseMacros = false },
        extensions = new()
        {
            options = ImmutableDictionary<string, PluginSettingsConfiguration>.Empty.Add("canonical.settings", new()
            { values = JsonSerializer.Deserialize<JsonElement>(settings) })
        }
    };
    private static string EmissionStatus(GenerationResult result) => Assert.Single(result.cacheStages.Where(stage => stage.stage == "csharpEmission")).status;
    private static string[] ManagedBytes(GenerationResult result) => result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
        .OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal).Select(artifact => artifact.relativePath + ":" + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(result.outputRoot, artifact.relativePath)))).ToArray();
    private sealed class SettingsPlugin : IBindingPlugin, ICacheFingerprintProvider
    {
        public PluginDescriptor descriptor => new("canonical.settings", "4.0.0", [new("canonical.transform", ExtensionPort.NativeModuleTransform)])
        { configurationSchema = JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = true }) };
        public void Configure(
            IExtensionRegistryBuilder registry,
            PluginConfiguration configuration
        )
            => registry.RegisterFactory<INativeModuleTransform>("canonical.transform", _ => new IdentityTransform());
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        ) => new(new(new string('a', 64)), []);
    }
    private sealed class IdentityTransform : INativeModuleTransform, ICacheFingerprintProvider
    {
        public NativeModule Transform(
            NativeModule module,
            AnalysisContext context,
            CancellationToken cancellationToken
        ) => module;
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        ) => new(new(new string('b', 64)), []);
    }
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
