using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Bridge;
using BGCS.Core.Inputs;
using BGCS.NativeBridge.Emission;
using BGCS.Infrastructure.Publishing;
using Xunit;

namespace BGCS.Tests;

public sealed class NeutralFrozenBridgeTests
{
    [Fact]
    public async Task SerializedNeutralBridgeEmitsIdenticalNativeArtifactsAfterOriginalSourceDeletion()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-frozen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "counter.hpp");
            await File.WriteAllTextAsync(header, "class Counter { int m_value; public: explicit Counter(int value) : m_value(value) {} int Add(int value) const { return value + m_value; } };");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "frozen", nativeName = "frozen_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["counter.hpp"], parseMacros = false },
                api = new() { @namespace = "Generated.Frozen", name = "FrozenApi" }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult generated = await host.GenerateAsync(new(configuration, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            NativeBridgePlan original = Assert.Single(generated.targetModels).bridge!;
            NativeBridgePlan restored = JsonSerializer.Deserialize<NativeBridgePlan>(JsonSerializer.Serialize(original))!;
            string native = Path.Combine(generated.outputRoot, generated.targetModels[0].targetId, "native");
            File.Delete(header);
            string repeated = Path.Combine(root, "reemitted");
            var artifacts = new TypedBridgeEmitter().Emit(restored, new DirectoryArtifactSink(repeated, TestHostPaths.Create()), original.exportPrefix);
            Assert.Equal(2, artifacts.Length);
            foreach (var artifact in artifacts)
                Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(native, artifact.relativePath)), await File.ReadAllBytesAsync(Path.Combine(repeated, artifact.relativePath)));
            Assert.False(File.Exists(header));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
