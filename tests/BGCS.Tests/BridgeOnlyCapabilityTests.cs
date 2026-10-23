using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Tests;

public sealed class BridgeOnlyCapabilityTests
{
    [Fact]
    public async Task NativeBridgeDoesNotActivateManagedProvidersOrClaimRuntimeEvidence()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "typedef struct Value { int number; } Value; Value transform(Value input);");
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot())
                .ReplaceFactory<IManagedRuntimeProvider>("dotnet.execution", new("test.runtime", ExtensionPort.ManagedRuntimeProvider),
                    _ => throw new InvalidOperationException("Managed runtime activation was requested."))
                .ReplaceFactory<ICallAbiProvider>("dotnet.call-abi", new("test.abi", ExtensionPort.CallAbiProvider),
                    _ => throw new InvalidOperationException("Managed ABI activation was requested."))
                .Build();
            GenerationRequest request = new(new()
            {
                schemaVersion = 2,
                library = new() { id = "native_only", nativeName = "native_only" },
                source = new() { headers = ["native.h"], parseMacros = false },
                api = new() { runtime = new ManagedRuntimeId("private.runtime") }
            }, root);
            GenerationResult bridge = await host.BridgeAsync(request);
            Assert.True(bridge.success, bridge.GetDiagnosticSummary());
            Assert.NotEmpty(Assert.Single(bridge.nativeSources).sources);
            Assert.Null(Assert.Single(bridge.nativeSources).runtimeRequirements);
            Assert.NotEmpty(bridge.capabilities);
            Assert.All(bridge.capabilities, report =>
            {
                Assert.Null(report.runtimeId); Assert.Null(report.runtimeProfileId);
                Assert.Null(report.callAbi); Assert.Null(report.runtimeProfileDigest);
            });
            Assert.DoesNotContain(bridge.artifacts, artifact => artifact.mediaType == "text/x-csharp");
            string[] previous = bridge.artifacts.Select(artifact => Convert.ToBase64String(File.ReadAllBytes(Path.Combine(bridge.outputRoot, artifact.relativePath)))).ToArray();
            GenerationResult managed = await host.GenerateAsync(request);
            Assert.False(managed.success);
            Assert.False(managed.published);
            Assert.Contains("Managed runtime activation", managed.GetDiagnosticSummary(), StringComparison.Ordinal);
            Assert.Equal(previous, bridge.artifacts.Select(artifact => Convert.ToBase64String(File.ReadAllBytes(Path.Combine(bridge.outputRoot, artifact.relativePath)))).ToArray());
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
