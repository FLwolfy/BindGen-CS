using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class WorkerAssetDeploymentTests
{
    [Fact]
    public void Deployment_CopiesTheExactOwnedHostClosureAndDetectsAlteredInputs()
    {
        using Scratch scratch = new();
        WorkerAssetManifest manifest = scratch.CreateManifest();
        WorkerAssetDeployer deployer = new();
        deployer.Deploy(scratch.source, scratch.output, manifest, "win-x64");
        Assert.Equal(manifest.artifacts.Length + 2, Directory.GetFiles(scratch.output, "*", SearchOption.AllDirectories).Length);
        Assert.True(File.Exists(Path.Combine(scratch.output, "worker-assets.manifest.json")));
        string entry = Path.Combine(scratch.output, "BGCS.Frontend.Clang.Worker.dll");
        string previous = File.ReadAllText(entry);
        File.WriteAllText(Path.Combine(scratch.source, "BGCS.Frontend.Clang.Worker.dll"), "tampered");
        Assert.Throws<IOException>(() => deployer.Deploy(scratch.source, scratch.output, manifest, "win-x64"));
        Assert.Equal(previous, File.ReadAllText(entry));
    }

    [Fact]
    public void Deployment_SelectsTheGeneratorHostAndRequiresBothNativeParserLibraries()
    {
        using Scratch scratch = new();
        WorkerAssetManifest manifest = scratch.CreateManifest();
        WorkerAssetDeployer deployer = new();
        Assert.Throws<IOException>(() => deployer.Deploy(scratch.source, scratch.output, manifest, "win-arm64"));
        WorkerAssetManifest missing = manifest with { artifacts = manifest.artifacts.Where(artifact => !artifact.relativePath.EndsWith("libClangSharp.dll", StringComparison.Ordinal)).ToImmutableArray() };
        Assert.Throws<IOException>(() => deployer.Deploy(scratch.source, scratch.output, missing, "win-x64"));
        Assert.False(Directory.Exists(scratch.output));
    }

    [Fact]
    public void Deployment_LocksLocalizedManagedRidAndNonParserNativeExecutionAssets()
    {
        using Scratch scratch = new();
        WorkerAssetManifest manifest = scratch.CreateManifest();
        string dependencyJson = """
            {"runtimeTarget":{"name":"net10"},"targets":{"net10":{"Execution/1.0":{
              "runtime":{"lib/net10.0/Execution.dll":{}},
              "resources":{"lib/net10.0/fr/Execution.resources.dll":{"locale":"fr"}},
              "runtimeTargets":{
                "runtimes/win/lib/net10.0/Execution.Win.dll":{"rid":"win","assetType":"runtime"},
                "runtimes/win-x64/native/auxiliary.dll":{"rid":"win-x64","assetType":"native"}
              }
            }}}}
            """;
        manifest = manifest with { artifacts = manifest.artifacts.Where(artifact => artifact.relativePath != "BGCS.Frontend.Clang.Worker.deps.json").ToImmutableArray()
            .Add(scratch.Add("BGCS.Frontend.Clang.Worker.deps.json", dependencyJson))
            .Add(scratch.Add("Execution.dll", "portable"))
            .Add(scratch.Add("fr/Execution.resources.dll", "localized"))
            .Add(scratch.Add("runtimes/win/lib/net10.0/Execution.Win.dll", "rid-specific-managed"))
            .Add(scratch.Add("runtimes/win-x64/native/auxiliary.dll", "native-dependency")) };
        WorkerAssetDeployer deployer = new();
        deployer.Deploy(scratch.source, scratch.output, manifest, "win-x64");
        deployer.VerifyDeployment(scratch.output, manifest, "win-x64");
        Assert.Equal("portable", File.ReadAllText(Path.Combine(scratch.output, "Execution.dll")));
        Assert.Equal("rid-specific-managed", File.ReadAllText(Path.Combine(scratch.output, "runtimes/win/lib/net10.0/Execution.Win.dll")));
        foreach (string required in new[] { "fr/Execution.resources.dll", "runtimes/win/lib/net10.0/Execution.Win.dll", "runtimes/win-x64/native/auxiliary.dll" })
        {
            WorkerAssetManifest omitted = manifest with { artifacts = manifest.artifacts.Where(artifact => artifact.relativePath != required).ToImmutableArray() };
            Assert.Throws<IOException>(() => deployer.VerifyDeployment(scratch.output, omitted, "win-x64"));
        }
    }

    [Theory]
    [InlineData("runtimeTargets", "runtimes/win/lib/net10.0/X.dll", "{\"rid\":\"win-arm64\",\"assetType\":\"runtime\"}")]
    [InlineData("resources", "lib/net10.0/X.resources.dll", "{\"locale\":\"../outside\"}")]
    [InlineData("runtime", "../outside/X.dll", "{}")]
    public void Deployment_RejectsEscapingOrInconsistentDependencyMetadata(string category, string path, string metadata)
    {
        using Scratch scratch = new();
        WorkerAssetManifest manifest = scratch.CreateManifest();
        string json = "{\"runtimeTarget\":{\"name\":\"net10\"},\"targets\":{\"net10\":{\"X/1.0\":{\"" + category + "\":{\"" + path + "\":" + metadata + "}}}}}";
        manifest = manifest with { artifacts = manifest.artifacts.Where(artifact => artifact.relativePath != "BGCS.Frontend.Clang.Worker.deps.json").ToImmutableArray()
            .Add(scratch.Add("BGCS.Frontend.Clang.Worker.deps.json", json)) };
        Assert.Throws<IOException>(() => new WorkerAssetDeployer().Deploy(scratch.source, scratch.output, manifest, "win-x64"));
        Assert.False(Directory.Exists(scratch.output));
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-worker-assets-" + Guid.NewGuid().ToString("N"));
        internal string source => Path.Combine(m_root, "source");
        internal string output => Path.Combine(m_root, "worker");
        internal Scratch() => Directory.CreateDirectory(source);
        internal ArtifactDescriptor Add(string relative, string content)
        {
            string path = Path.Combine(source, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return new(relative, "application/octet-stream", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))));
        }
        internal WorkerAssetManifest CreateManifest()
        {
            string[] names = ["BGCS.Frontend.Clang.Worker.dll", "BGCS.Frontend.Clang.Worker.deps.json", "BGCS.Frontend.Clang.Worker.runtimeconfig.json", "BGCS.Contracts.dll", "BGCS.Infrastructure.dll", "BGCS.Frontend.Clang.dll", "ClangSharp.dll", "ClangSharp.Interop.dll", "runtimes/win-x64/native/libclang.dll", "runtimes/win-x64/native/libClangSharp.dll"];
            return new(1, "win-x64", WorkerProtocol.C_PARSER_IDENTITY, names.Select(name =>
            {
                string content = "fixture:" + name;
                if (name.EndsWith(".deps.json", StringComparison.Ordinal)) content = "{\"runtimeTarget\":{\"name\":\"net10\"},\"targets\":{\"net10\":{}}}";
                string path = Path.Combine(source, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content, new UTF8Encoding(false));
                return new ArtifactDescriptor(name, "application/octet-stream", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))));
            }).ToImmutableArray());
        }
        public void Dispose() => Directory.Delete(m_root, recursive: true);
    }
}
