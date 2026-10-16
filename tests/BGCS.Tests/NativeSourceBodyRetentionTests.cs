using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeSourceBodyRetentionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeProofAndStandaloneReplayPreserveExactBodyModeWithImplicitDestructors(bool retainBodies)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs native body mode " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.hpp"), """
                #pragma once
                namespace body {
                struct Leaf { ~Leaf() {} };
                template<class T> struct Box { T value; };
                struct Defaulted { Defaulted() = default; ~Defaulted() = default; };
                inline Box<Leaf> make_box() { return {}; }
                inline int answer() { return 42; }
                inline auto deduced() { return 42; }
                }
                int Collect(int first, ...);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "native.cpp"), """
                #include "native.hpp"
                #include <stdarg.h>
                int Collect(int first, ...) {
                    va_list values; va_start(values, first);
                    int result = first + va_arg(values, int); va_end(values);
                    return result;
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "body_mode", nativeName = "body_mode" },
                source = new()
                {
                    language = SourceLanguage.Cpp, headers = ["native.hpp"], parseMacros = false,
                    includeTransitivelyReferencedHeaders = false,
                    selection = new() { includeDeclarations = retainBodies
                        ? ["body::make_box", "body::answer", "body::deduced", "body::Defaulted", "Collect(int, ...)"]
                        : ["body::make_box", "body::answer", "body::deduced", "body::Defaulted"] },
                    variadicVariants = retainBodies ? [new()
                    {
                        function = "Collect(int, ...)", name = "CollectValues", parameters = [new("extra", "int")]
                    }] : []
                },
                runtime = new() { mode = "source" }, cache = new() { enabled = false },
                nativeBuild = new() { mode = "sources", sources = ["native.cpp"] }
            };
            ConcurrentQueue<(FrontendRequestPurpose purpose, bool retainBodies)> calls = new();
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns()
                .WithFrontendFactory(() => new RecordingFrontend(WorkerRoot(), calls)).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.generation.targetModels);
            Assert.Contains(target.native.declarations, declaration => declaration.name == "~Box" && declaration.isDefinition
                && declaration.parent is { } owner && target.native.GetDeclaration(owner).qualifiedName.StartsWith("body::Box<", StringComparison.Ordinal));
            Assert.Contains(target.native.declarations, declaration => declaration.qualifiedName == "body::Defaulted::Defaulted" && declaration.isDefinition);
            Assert.Contains(target.native.declarations, declaration => declaration.qualifiedName == "body::Defaulted::~Defaulted" && declaration.isDefinition);
            Assert.True(Assert.Single(target.native.declarations, declaration => declaration.qualifiedName == "body::answer").isDefinition);
            Assert.True(Assert.Single(target.native.declarations, declaration => declaration.qualifiedName == "body::deduced").isDefinition);
            Assert.Contains(calls, call => call.purpose == FrontendRequestPurpose.SourceAnalysis && call.retainBodies == retainBodies);
            AssertProofMode();

            string manifestPath = Assert.Single(Directory.GetFiles(built.outputRoot, "bridge.manifest.json", SearchOption.AllDirectories));
            byte[] originalManifest = await File.ReadAllBytesAsync(manifestPath);
            NativeBuildManifest manifest = JsonSerializer.Deserialize<NativeBuildManifest>(originalManifest, WorkerProtocol.jsonOptions)!;
            Assert.Equal(retainBodies, manifest.sourceRetainFunctionBodies);
            while (calls.TryDequeue(out _)) { }
            ManifestNativeBuildResult standalone = await host.NativeBuildAsync(new(manifestPath));
            Assert.True(standalone.success, standalone.GetDiagnosticSummary());
            AssertProofMode();
            CppBridgeModule bridge = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(
                Path.Combine(Path.GetDirectoryName(manifestPath)!, "bridge.module.json")), WorkerProtocol.jsonOptions)!;
            string answer = Assert.Single(bridge.exports, export => export.operation.declaration is { } declaration
                && bridge.source.GetDeclaration(declaration).qualifiedName == "body::answer").name;
            nint native = NativeLibrary.Load(standalone.nativeArtifact!);
            try
            {
                Assert.Equal(0, Marshal.GetDelegateForFunctionPointer<Answer>(NativeLibrary.GetExport(native, answer))(out int value, out nint failure));
                Assert.Equal(42, value);
                Assert.Equal(0, failure);
            }
            finally { NativeLibrary.Free(native); }
            var before = Directory.GetFiles(built.outputRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            foreach (bool missing in new[] { true, false })
            {
                JsonObject incomplete = JsonNode.Parse(originalManifest)!.AsObject();
                if (missing) Assert.True(incomplete.Remove("sourceRetainFunctionBodies"));
                else incomplete["sourceRetainFunctionBodies"] = null;
                await File.WriteAllTextAsync(manifestPath, incomplete.ToJsonString());
                ManifestNativeBuildResult rejected = await host.NativeBuildAsync(new(manifestPath));
                Assert.False(rejected.success);
                Assert.False(rejected.published);
                Assert.Contains("sourceRetainFunctionBodies", rejected.GetDiagnosticSummary());
                await File.WriteAllBytesAsync(manifestPath, originalManifest);
                Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(built.outputRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
                foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
            }

            void AssertProofMode()
            {
                var proofs = calls.Where(call => call.purpose == FrontendRequestPurpose.NativeBuildVerification).ToArray();
                Assert.NotEmpty(proofs);
                Assert.All(proofs, proof => Assert.Equal(retainBodies, proof.retainBodies));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class RecordingFrontend(string workerRoot, ConcurrentQueue<(FrontendRequestPurpose purpose, bool retainBodies)> calls) : IFrontend
    {
        private readonly FrontendWorkerClient m_worker = new(Path.Combine(workerRoot, "BGCS.Frontend.Clang.Worker.dll"));
        public ValueTask<FrontendResult> ParseAsync(FrontendRequest request, CancellationToken cancellationToken)
        {
            calls.Enqueue((request.purpose, request.retainFunctionBodies));
            return m_worker.ParseAsync(request, cancellationToken);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Answer(out int value, out nint failure);
    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
