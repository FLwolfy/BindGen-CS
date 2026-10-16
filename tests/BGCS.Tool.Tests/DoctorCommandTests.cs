using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class DoctorCommandTests
{
    [Fact]
    public void BareDoctor_RejectsMissingWorkerWithoutClaimingParsing()
    {
        using Scenario scenario = new();
        using JsonDocument result = scenario.Doctor(1);
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("missing", result.RootElement.GetProperty("worker").GetProperty("status").GetString());
        Assert.False(result.RootElement.GetProperty("worker").GetProperty("parseExecuted").GetBoolean());
        Assert.False(result.RootElement.GetProperty("nativeBuildExecuted").GetBoolean());
    }

    [Fact]
    public void ThreeEntryFiles_DoNotCertifyNativeParserClosure()
    {
        using Scenario scenario = new();
        scenario.WriteUnlockedEntries();
        using JsonDocument result = scenario.Doctor(1);
        Assert.Equal("unlocked", result.RootElement.GetProperty("worker").GetProperty("status").GetString());
        Assert.False(result.RootElement.GetProperty("worker").GetProperty("assetDigestsVerified").GetBoolean());
    }

    [Fact]
    public void BareDoctor_VerifiesCompleteLockedAssetsWithoutExecutingFixtureBinaries()
    {
        using Scenario scenario = new();
        scenario.WriteFixtureClosure();
        using JsonDocument result = scenario.Doctor(0);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("verified", result.RootElement.GetProperty("worker").GetProperty("status").GetString());
        Assert.True(result.RootElement.GetProperty("worker").GetProperty("assetDigestsVerified").GetBoolean());
        Assert.False(result.RootElement.GetProperty("worker").GetProperty("parseExecuted").GetBoolean());
        Assert.False(result.RootElement.GetProperty("targetValidated").GetBoolean());
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("wrongHost")]
    [InlineData("missingNative")]
    public void BareDoctor_RejectsCorruptedOrIncompleteLockedAssets(string failure)
    {
        using Scenario scenario = new();
        WorkerAssetManifest manifest = scenario.WriteFixtureClosure();
        if (failure == "tampered")
            File.AppendAllText(Path.Combine(scenario.worker, "BGCS.Contracts.dll"), "changed");
        else
        {
            manifest = failure == "wrongHost" ? manifest with { hostRid = "unrelated-host" }
                : manifest with { artifacts = manifest.artifacts.Where(item => !item.relativePath.Contains("libClangSharp", StringComparison.Ordinal)).ToImmutableArray() };
            scenario.WriteManifest(manifest);
        }
        using JsonDocument result = scenario.Doctor(1);
        Assert.Equal("invalid", result.RootElement.GetProperty("worker").GetProperty("status").GetString());
        Assert.False(result.RootElement.GetProperty("worker").GetProperty("assetDigestsVerified").GetBoolean());
    }

    [Fact]
    public void ExplicitMissingConfiguration_IsNeverTreatedAsBareSuccess()
    {
        using Scenario scenario = new();
        scenario.WriteFixtureClosure();
        using JsonDocument result = scenario.Doctor(1, "missing.json");
        Assert.Contains(result.RootElement.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "BGCS-DOCTOR-CONFIGURATION");
    }

    [Fact]
    public void ConfiguredDoctor_ReusesResolvedTargetWithoutRequiringUnusedBuildTools()
    {
        using Scenario scenario = new();
        scenario.CopyActualWorker();
        File.WriteAllText(Path.Combine(scenario.root, "native.h"), "int native_value(void);\n");
        File.WriteAllText(Path.Combine(scenario.root, "bindgen.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, library = new { id = "doctor_probe", nativeName = "doctor_probe" },
            source = new { language = "c", headers = new[] { "native.h" } },
            nativeBuild = new { mode = "none", provider = "cmake", buildToolPath = "unused-missing-cmake" },
            cache = new { enabled = false }
        }));
        using JsonDocument result = scenario.Doctor(0, "bindgen.json");
        Assert.True(result.RootElement.GetProperty("inspectionExecuted").GetBoolean());
        Assert.True(result.RootElement.GetProperty("targetValidated").GetBoolean());
        Assert.False(result.RootElement.GetProperty("nativeBuildExecuted").GetBoolean());
        JsonElement prerequisite = Assert.Single(result.RootElement.GetProperty("prerequisites").EnumerateArray());
        Assert.Equal("notRequested", prerequisite.GetProperty("status").GetString());
        JsonElement target = Assert.Single(result.RootElement.GetProperty("inspection").GetProperty("targets").EnumerateArray());
        Assert.Equal(JsonValueKind.Object, target.GetProperty("resolvedTarget").ValueKind);
        Assert.False(Directory.Exists(Path.Combine(scenario.root, "Generated")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfiguredDoctor_RejectsMissingOrWrongBuildToolWithoutPublishing(bool wrongTool)
    {
        using Scenario scenario = new();
        scenario.CopyActualWorker();
        string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        File.WriteAllText(Path.Combine(scenario.root, "native.h"), "int native_add(int left, int right);\n");
        File.WriteAllText(Path.Combine(scenario.root, "native.c"), "int native_add(int left, int right) { return left + right; }\n");
        File.WriteAllText(Path.Combine(scenario.root, "bindgen.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, library = new { id = "doctor_probe", nativeName = "doctor_probe" },
            source = new { language = "c", headers = new[] { "native.h" } },
            nativeBuild = new
            {
                mode = "sources", provider = "cmake", sources = new[] { "native.c" },
                buildToolPath = wrongTool ? host : Path.Combine(scenario.root, "missing-cmake"),
                auxiliaryToolPath = host
            },
            cache = new { enabled = false }
        }));
        using JsonDocument result = scenario.Doctor(1, "bindgen.json");
        JsonElement readiness = Assert.Single(result.RootElement.GetProperty("prerequisites").EnumerateArray());
        Assert.False(readiness.GetProperty("ready").GetBoolean());
        Assert.Equal("unavailable", readiness.GetProperty("status").GetString());
        Assert.False(result.RootElement.GetProperty("nativeBuildExecuted").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(scenario.root, "Generated")));
        if (wrongTool)
            Assert.Contains(result.RootElement.GetProperty("diagnostics").EnumerateArray(),
                item => item.GetProperty("message").GetString()!.Contains("numeric version", StringComparison.Ordinal));
    }

    private sealed class Scenario : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-doctor-" + Guid.NewGuid().ToString("N"));
        internal string worker => Path.Combine(root, "bgcs", "worker");

        internal Scenario()
        {
            Directory.CreateDirectory(root);
            foreach (string source in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
                File.Copy(source, Path.Combine(root, Path.GetFileName(source)));
            foreach (string name in new[] { "BGCS.Tool.deps.json", "BGCS.Tool.runtimeconfig.json" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(root, name));
        }

        internal JsonDocument Doctor(int expected, params string[] arguments)
        {
            using StringWriter output = new();
            using StringWriter error = new();
            int actual = CliInvocation.Run(["doctor", .. arguments, "--json"], root, output, error,
                Path.Combine(root, "BGCS.Tool.dll"));
            Assert.True(expected == actual, $"Doctor expected exit {expected}, received {actual}.\nstdout: {output}\nstderr: {error}");
            return JsonDocument.Parse(output.ToString());
        }

        internal void WriteUnlockedEntries()
        {
            Directory.CreateDirectory(worker);
            foreach (string suffix in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
                File.WriteAllText(Path.Combine(worker, "BGCS.Frontend.Clang.Worker" + suffix), "fixture");
        }

        internal WorkerAssetManifest WriteFixtureClosure()
        {
            string rid = RuntimeInformation.RuntimeIdentifier;
            string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
            string[] entries = ["BGCS.Frontend.Clang.Worker.dll", "BGCS.Frontend.Clang.Worker.deps.json", "BGCS.Frontend.Clang.Worker.runtimeconfig.json",
                "BGCS.Contracts.dll", "BGCS.Infrastructure.dll", "BGCS.Frontend.Clang.dll", "ClangSharp.dll", "ClangSharp.Interop.dll",
                "runtimes/" + rid + "/native/libclang" + extension, "runtimes/" + rid + "/native/libClangSharp" + extension];
            foreach (string name in entries)
            {
                string destination = Path.Combine(worker, name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllText(destination, name.EndsWith(".deps.json", StringComparison.Ordinal)
                    ? "{\"runtimeTarget\":{\"name\":\"net10\"},\"targets\":{\"net10\":{}}}" : "fixture:" + name);
            }
            return WriteCurrentManifest(rid);
        }

        internal void CopyActualWorker()
        {
            string source = Path.Combine(AppContext.BaseDirectory, "bgcs", "worker");
            foreach (string input in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(input) is "worker-assets.manifest.json" or ".bgcs-artifacts.json") continue;
                string destination = Path.Combine(worker, Path.GetRelativePath(source, input));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(input, destination);
            }
            _ = WriteCurrentManifest(RuntimeInformation.RuntimeIdentifier);
        }

        private WorkerAssetManifest WriteCurrentManifest(string rid)
        {
            WorkerAssetManifest manifest = new(1, rid, WorkerProtocol.C_PARSER_IDENTITY,
                Directory.EnumerateFiles(worker, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetFileName(path) != "worker-assets.manifest.json")
                    .Select(path => new ArtifactDescriptor(Path.GetRelativePath(worker, path).Replace('\\', '/'), "application/octet-stream",
                        new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))))).ToImmutableArray());
            WriteManifest(manifest);
            return manifest;
        }

        internal void WriteManifest(WorkerAssetManifest manifest)
            => File.WriteAllText(Path.Combine(worker, "worker-assets.manifest.json"), JsonSerializer.Serialize(manifest));

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
