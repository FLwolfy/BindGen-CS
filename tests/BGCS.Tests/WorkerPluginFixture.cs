using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Workers;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Configuration;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Plugins;
using BGCS.Infrastructure.Workers;

namespace BGCS.Tests;

internal sealed class WorkerPluginFixture : IDisposable
{
    internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-host-worker-plugin-" + Guid.NewGuid().ToString("N"));
    internal string source => Path.Combine(root, "source");
    internal string worker => Path.Combine(root, "worker");
    internal string plugin => Path.Combine(root, "plugin");
    internal string settings => Path.Combine(root, "settings");
    internal string manifest => Path.Combine(plugin, "plugin.json");
    internal string external => Path.Combine(settings, "external.txt");
    internal string output => Path.Combine(root, "output");

    internal WorkerPluginFixture(bool fingerprinted = true)
    {
        foreach (string directory in new[] { source, worker, plugin, settings }) Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(source, "native.h"), "int read_value(void);\n");
        File.WriteAllText(external, "first");
        string repository = Repository();
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        string fixture = Path.Combine(repository, "tests", "fixtures", "plugins", "ClangFactPlugin", "bin", configuration, "net10.0");
        string[] names = ["ClangFactPlugin.dll", "ClangFactPlugin.deps.json"];
        foreach (string name in names) File.Copy(Path.Combine(fixture, name), Path.Combine(plugin, name));
        PluginManifest entry = new()
        {
            schemaVersion = 1,
            assembly = names[0],
            entryType = fingerprinted ? "ClangFactFixture.FingerprintedFactPlugin" : "ClangFactFixture.FactPlugin",
            workerOnly = true,
            workerParserIdentity = ClangParserIdentity.C_IDENTITY,
            plugin = new("clang.fixture", "1.0.0", [new("clang.fixture.facts", ExtensionPort.FrontendFactContributor)])
            {
                configurationSchema = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "externalFile" },
                    properties = new { externalFile = new { type = "string", minLength = 1 }, mode = new { type = "string" } }
                })
            },
            assets = names.Select(name => new PluginAsset(name, Digest(Path.Combine(plugin, name)))).ToImmutableArray()
        };
        WriteManifest(entry);
        string compiled = Path.Combine(repository, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
        foreach (string file in Directory.GetFiles(compiled)) File.Copy(file, Path.Combine(worker, Path.GetFileName(file)));
        string native = Path.Combine("runtimes", RuntimeInformation.RuntimeIdentifier, "native");
        Directory.CreateDirectory(Path.Combine(worker, native));
        foreach (string file in Directory.GetFiles(Path.Combine(compiled, native))) File.Copy(file, Path.Combine(worker, native, Path.GetFileName(file)));
        WorkerAssetManifest assets = new(1, RuntimeInformation.RuntimeIdentifier, ClangParserIdentity.C_IDENTITY,
            Directory.GetFiles(worker, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(path => new ArtifactDescriptor(Path.GetRelativePath(worker, path).Replace('\\', '/'), "application/octet-stream", Digest(path))).ToImmutableArray());
        File.WriteAllText(Path.Combine(worker, "worker-assets.manifest.json"), JsonSerializer.Serialize(assets));
    }

    internal void WriteManifest(PluginManifest entry) => File.WriteAllText(manifest, JsonSerializer.Serialize(entry,
        new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }));
    internal JsonElement Values(string mode = "normal") => JsonSerializer.SerializeToElement(new { externalFile = "external.txt", mode });
    internal BindingHostBuilder Builder(string mode = "normal") => BindingGenerator.CreateBuilder().UseWorkerDirectory(worker)
        .AddPluginManifest(manifest, TestHostPaths.Create(), Values(mode), settings);
    internal GenerationRequest Request() => new(new BindingConfiguration
    {
        schemaVersion = 2,
        library = new() { id = "worker_host", nativeName = "worker_host" },
        source = new() { headers = ["native.h"], parseMacros = false },
        api = new() { @namespace = "Generated.WorkerHost", name = "WorkerApi" },
        output = new() { root = output },
        cache = new() { enabled = true, directory = Path.Combine(root, "cache") }
    }, source);
    private static ContentDigest Digest(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return new(Convert.ToHexStringLower(SHA256.HashData(stream)));
    }
    private static string Repository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The fixture needs a repository checkout.");
    }
    public void Dispose() => Directory.Delete(root, recursive: true);
}
