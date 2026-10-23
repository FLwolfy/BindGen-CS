using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class GeneratedModelJsonTests
{
    [Fact]
    public async Task PublishedModelJsonPreservesDefaultReflectionSerialization()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-model-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                enum Mode { Zero = 0, One = 1 };
                /** Documentation λ😀 <&> with explicit empty/default contracts. */
                int echo(enum Mode value);
                """, new UTF8Encoding(false));
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "model_json", nativeName = "model_json" },
                source = new() { headers = ["native.h"], parseMacros = false },
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult first = await host.GenerateAsync(new(configuration, root));
            Assert.True(first.success, first.GetDiagnosticSummary());
            Assert.Equal("miss", Assert.Single(first.cacheStages, stage => stage.stage == "csharpEmission").status);
            TargetInspection target = Assert.Single(first.targetModels);
            Assert.Null(target.bridge);
            Assert.Contains("λ😀", Assert.Single(target.interop.functions).documentation);
            Assert.Contains(target.interop.types, type => type.category == NativeTypeCategory.Enumeration);
            AssertModels(first);

            string[] metadata = MetadataBytes(first);
            Assert.True(await host.ClearOwnedOutputAsync(new(configuration, root)));
            GenerationResult warm = await host.GenerateAsync(new(configuration, root));
            Assert.True(warm.success, warm.GetDiagnosticSummary());
            Assert.Equal("hit", Assert.Single(warm.cacheStages, stage => stage.stage == "csharpEmission").status);
            AssertModels(warm);
            Assert.Equal(metadata, MetadataBytes(warm));

            GenerationResult uncached = await host.GenerateAsync(new(configuration with { cache = configuration.cache with { enabled = false } }, root));
            Assert.True(uncached.success, uncached.GetDiagnosticSummary());
            Assert.Equal("disabled", Assert.Single(uncached.cacheStages, stage => stage.stage == "csharpEmission").status);
            AssertModels(uncached);
            Assert.Equal(metadata, MetadataBytes(uncached));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void AssertModels(GenerationResult result)
    {
        TargetInspection target = Assert.Single(result.targetModels);
        using JsonDocument abi = Read(result, target.targetId + "/metadata/abi.manifest.json");
        JsonElement module = abi.RootElement.GetProperty("module");
        Assert.Equal(JsonSerializer.Serialize(target.interop), Compact(module));
        JsonElement function = Assert.Single(module.GetProperty("functions").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, function.GetProperty("returnOwnership").ValueKind);
        Assert.False(function.GetProperty("isVariadic").GetBoolean());
        Assert.True(function.GetProperty("hasPrototype").GetBoolean());
        Assert.Equal(JsonValueKind.Number, Assert.Single(module.GetProperty("types").EnumerateArray(),
            type => type.GetProperty("category").GetInt32() == (int)NativeTypeCategory.Enumeration).GetProperty("category").ValueKind);
        using JsonDocument manifest = Read(result, target.targetId + "/metadata/native-source.manifest.json");
        Assert.Equal(JsonSerializer.Serialize(Assert.Single(result.nativeSources)), Compact(manifest.RootElement));
        using JsonDocument capabilities = Read(result, target.targetId + "/metadata/capability.report.json");
        Assert.Equal(JsonSerializer.Serialize(result.capabilities), Compact(capabilities.RootElement));
    }

    private static JsonDocument Read(GenerationResult result, string relativePath)
        => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(result.outputRoot, relativePath)));

    private static string Compact(JsonElement value)
    {
        using MemoryStream bytes = new();
        using (Utf8JsonWriter writer = new(bytes)) value.WriteTo(writer);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static string[] MetadataBytes(GenerationResult result) => result.artifacts
        .Where(artifact => artifact.relativePath.EndsWith("/metadata/abi.manifest.json", StringComparison.Ordinal)
            || artifact.relativePath.EndsWith("/metadata/native-source.manifest.json", StringComparison.Ordinal)
            || artifact.relativePath.EndsWith("/metadata/capability.report.json", StringComparison.Ordinal))
        .OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal)
        .Select(artifact => artifact.relativePath + ":" + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(result.outputRoot, artifact.relativePath)))).ToArray();

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
