using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class ExplicitTargetTripleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SdkAndGroupedGenerationPreserveAnExplicitCompatibleTriple(bool generation)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs explicit target triple " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "/* synthetic frontend boundary fixture */");
            CapturingFrontend frontend = new();
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithFrontendFactory(() => frontend).Build();
            const string triple = "x86_64-unknown-freebsd14.2";
            if (generation)
            {
                BindingConfiguration configuration = new()
                {
                    schemaVersion = 1, library = new() { id = "triple", nativeName = "triple" }, source = new() { headers = ["native.h"] },
                    targets = [new() { id = "freebsd-x64-gnu", triple = triple }], cache = new() { enabled = false }
                };
                string path = Path.Combine(root, "bindgen.json");
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(configuration, new JsonSerializerOptions
                    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }));
                BindingConfiguration loaded = new BindingConfigurationLoader().Load(path);
                Assert.Equal(triple, Assert.Single(loaded.targets).triple);
                GenerationResult result = await host.GenerateAsync(new(loaded, root));
                Assert.True(result.success, result.GetDiagnosticSummary());
                Assert.True(new SchemaCatalog().CreateBindingSchema()["properties"]!["targets"]!["items"]!["properties"]!.AsObject().ContainsKey("triple"));
            }
            else
            {
                AnalysisResult result = await host.AnalyzeAsync(AnalysisRequest.FromFiles(["native.h"], baseDirectory: root) with { targetId = "freebsd-x64-gnu", triple = triple });
                Assert.True(result.success, result.GetDiagnosticSummary());
                Assert.Equal(triple, result.target!.triple);
            }
            Assert.Equal(triple, Assert.IsType<FrontendRequest>(frontend.request).target.triple);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IncompatibleTripleFailsBeforeFrontendCreation()
    {
        bool created = false;
        await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithFrontendFactory(() => { created = true; return new CapturingFrontend(); }).Build();
        AnalysisResult result = await host.AnalyzeAsync(AnalysisRequest.FromFiles(["native.h"]) with { targetId = "linux-x64-gnu", triple = "x86_64-unknown-linux-musl" });
        Assert.False(result.success);
        Assert.False(created);
        Assert.Contains("incompatible", result.GetDiagnosticSummary());
    }

    private sealed class CapturingFrontend : IFrontend
    {
        internal FrontendRequest? request;
        public ValueTask<FrontendResult> ParseAsync(FrontendRequest input, CancellationToken cancellationToken)
        {
            request = input;
            NativeModule module = new(input.target.targetId.value, input.target.triple, 8, [], [], []) { language = input.language, byteOrder = input.target.byteOrder };
            return ValueTask.FromResult(new FrontendResult(module, new SourceSnapshot([], [])));
        }
    }
}
