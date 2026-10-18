using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Plugins;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms.Targeting;
using Xunit;
using ExamplePlugin = BGCS.Examples.CustomTargetPlugin.CustomTargetPlugin;

namespace BGCS.Tool.Tests;

public sealed class CustomTargetPluginCommandTests
{
    [Fact]
    public void ManifestTargetUsesTheRealCliWorkerAndTracksItsSdkBeforeWarmReuse()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-cli-private-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sdk", "include"));
        try
        {
            string toolPath = DeployTool(root);
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
            string example = Path.Combine(repository!.FullName, "examples", "CustomTargetPlugin");
            File.Copy(Path.Combine(example, "native.h"), Path.Combine(root, "native.h"));
            string configuration = Path.Combine(root, "bindgen.json");
            File.Copy(Path.Combine(example, "bindgen.json"), configuration);
            var host = new TargetResolver().Resolve(new(new("host")));
            File.WriteAllText(Path.Combine(root, "target-sdk.json"), JsonSerializer.Serialize(new
            {
                platformId = "private-desktop", host.architectureId, host.abiId, host.triple,
                byteOrder = "littleEndian", requiresExplicitSdk = true, supportsDynamicModules = true,
                systemIncludeDirectories = new[] { "sdk/include" }, defines = new[] { "PROFILE_VERSION=1" }
            }));
            string sdkHeader = Path.Combine(root, "sdk", "include", "private_sdk.h");
            File.WriteAllText(sdkHeader, "#define EXAMPLE_SDK_VALUE 42\n");
            byte[] entry = File.ReadAllBytes(typeof(ExamplePlugin).Assembly.Location);
            File.WriteAllBytes(Path.Combine(root, "target-plugin.dll"), entry);
            PluginManifest manifest = new() { schemaVersion = 1, assembly = "target-plugin.dll", entryType = typeof(ExamplePlugin).FullName!,
                plugin = new ExamplePlugin().descriptor, assets = [new("target-plugin.dll", new(Convert.ToHexStringLower(SHA256.HashData(entry))))] };
            File.WriteAllText(Path.Combine(root, "plugin.json"), JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }));
            using JsonDocument first = Generate(root, toolPath);
            using JsonDocument firstInspection = Execute(root, toolPath, "inspect");
            Assert.Equal("example-private-host", firstInspection.RootElement.GetProperty("targets")[0].GetProperty("native").GetProperty("targetId").GetString());
            using JsonDocument warm = Generate(root, toolPath);
            Assert.Equal("hit", Stage(warm, "primaryFrontend"));
            Assert.Equal("hit", Stage(warm, "csharpEmission"));
            File.WriteAllText(sdkHeader, "#define EXAMPLE_SDK_VALUE 43\n");
            using JsonDocument changed = Generate(root, toolPath);
            Assert.NotEqual("hit", Stage(changed, "primaryFrontend"));
            using JsonDocument changedInspection = Execute(root, toolPath, "inspect");
            Assert.Contains(changedInspection.RootElement.GetProperty("targets")[0].GetProperty("native").GetProperty("declarations").EnumerateArray(),
                value => value.GetProperty("name").GetString() == "ExampleSdkLimit" && value.GetProperty("evaluatedIntegralValue").GetString() == "43");
            string output = changed.RootElement.GetProperty("outputRoot").GetString()!;
            var before = Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            JsonNode config = JsonNode.Parse(File.ReadAllText(configuration))!;
            config["api"]!.AsObject().Remove("runtime");
            File.WriteAllText(configuration, config.ToJsonString());
            using StringWriter error = new();
            Assert.NotEqual(0, CliInvocation.Run(["generate", configuration, "--json"], root, TextWriter.Null, error, toolPath));
            Assert.Contains("api.runtime", error.ToString());
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(output, "*", SearchOption.AllDirectories).Order());
            foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static JsonDocument Generate(string root, string toolPath)
        => Execute(root, toolPath, "generate");

    private static JsonDocument Execute(string root, string toolPath, string command)
    {
        using StringWriter output = new(), error = new();
        int exit = CliInvocation.Run([command, "bindgen.json", "--json"], root, output, error, toolPath);
        Assert.True(exit == 0, output + Environment.NewLine + error);
        JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        return document;
    }

    private static string DeployTool(string fixtureRoot)
    {
        string application = Path.Combine(fixtureRoot, "tool");
        Directory.CreateDirectory(application);
        foreach (string source in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
            File.Copy(source, Path.Combine(application, Path.GetFileName(source)));
        foreach (string name in new[] { "BGCS.Tool.deps.json", "BGCS.Tool.runtimeconfig.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(application, name));
        string sourceWorker = Path.Combine(AppContext.BaseDirectory, "bgcs", "worker");
        string worker = Path.Combine(application, "bgcs", "worker");
        foreach (string source in Directory.EnumerateFiles(sourceWorker, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(source) is "worker-assets.manifest.json" or ".bgcs-artifacts.json") continue;
            string destination = Path.Combine(worker, Path.GetRelativePath(sourceWorker, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        WorkerAssetManifest assets = new(1, RuntimeInformation.RuntimeIdentifier, WorkerProtocol.C_PARSER_IDENTITY,
            Directory.GetFiles(worker, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(file => new ArtifactDescriptor(Path.GetRelativePath(worker, file).Replace('\\', '/'), "application/octet-stream",
                    new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))))).ToImmutableArray());
        File.WriteAllText(Path.Combine(worker, "worker-assets.manifest.json"), JsonSerializer.Serialize(assets));
        return Path.Combine(application, "BGCS.Tool.dll");
    }

    private static string? Stage(JsonDocument document, string name) => document.RootElement.GetProperty("cacheStages").EnumerateArray()
        .Single(stage => stage.GetProperty("stage").GetString() == name).GetProperty("status").GetString();
}
