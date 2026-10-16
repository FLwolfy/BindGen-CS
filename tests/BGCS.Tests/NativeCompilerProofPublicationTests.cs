using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCompilerProofPublicationTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int BridgeValue(out int value, out nint error);

    [Theory]
    [InlineData("generate")]
    [InlineData("bridge")]
    [InlineData("build-none")]
    [InlineData("build-prebuilt")]
    [InlineData("build-sources")]
    public async Task RequiredNativeCompilerProofCannotBeSkippedByAnEntryPointOrReplacePublishedOutput(string entryPoint)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-required-native-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.hpp"), "namespace Proof { using Number = int; inline Number answer() { return 42; } }\n");
            NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, "clang");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1,
                library = new() { id = "proof-barrier", nativeName = "proof_barrier" },
                source = new() { language = SourceLanguage.Cpp, headers = ["native.hpp"], parseMacros = false },
                targets = [new() { settings = new() { compilerPath = target.toolchain.compilerPath } }],
                nativeBuild = new() { provider = "clang" },
                api = new() { @namespace = "Generated.RequiredProof", name = "ProofApi" },
                cache = new() { enabled = false }
            };
            State state = new();
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns()
                .WithFrontendFactory(() => new ProofFrontend(WorkerRoot(), state)).Build();
            GenerationResult previous = await host.GenerateAsync(new(configuration, root));
            Assert.True(previous.success, previous.GetDiagnosticSummary());
            if (entryPoint == "generate")
            {
                GenerationDiffResult unchanged = await host.DiffAsync(new(configuration, root));
                Assert.True(unchanged.success, string.Join(Environment.NewLine, unchanged.diagnostics.Select(diagnostic => diagnostic.message)));
                Assert.Empty(unchanged.changes);
                string targetId = Assert.Single(previous.targetModels).targetId;
                string manifestPath = Path.Combine(previous.outputRoot, targetId, "native", "metadata", "bridge.manifest.json");
                ManifestNativeBuildResult built = await host.NativeBuildAsync(new(manifestPath));
                Assert.True(built.success, built.GetDiagnosticSummary());
                CppBridgeModule bridge = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(
                    Path.Combine(Path.GetDirectoryName(manifestPath)!, "bridge.module.json")))!;
                string symbol = Assert.Single(bridge.exports, export => export.operation.kind == BridgeOperationKind.FreeCall).name;
                nint module = NativeLibrary.Load(built.nativeArtifact!);
                try
                {
                    Assert.Equal(0, Marshal.GetDelegateForFunctionPointer<BridgeValue>(NativeLibrary.GetExport(module, symbol))(out int value, out nint error));
                    Assert.Equal(42, value);
                    Assert.Equal(0, error);
                }
                finally { NativeLibrary.Free(module); }

                byte[] nativeBefore = await File.ReadAllBytesAsync(built.nativeArtifact!);
                string header = Path.Combine(root, "native.hpp");
                byte[] originalHeader = await File.ReadAllBytesAsync(header);
                await File.WriteAllTextAsync(header, (await File.ReadAllTextAsync(header)).Replace("return 42", "return 43", StringComparison.Ordinal));
                try
                {
                    ManifestNativeBuildResult changed = await host.NativeBuildAsync(new(manifestPath));
                    Assert.False(changed.success);
                    Assert.Equal(nativeBefore, await File.ReadAllBytesAsync(built.nativeArtifact!));
                }
                finally { await File.WriteAllBytesAsync(header, originalHeader); }
                string canonical = Path.Combine(previous.outputRoot, targetId, "native", "src", "canonical.assertions.cpp");
                byte[] originalCanonical = await File.ReadAllBytesAsync(canonical);
                await File.AppendAllTextAsync(canonical, "\n// unaccepted native proof mutation\n");
                try
                {
                    ManifestNativeBuildResult changed = await host.NativeBuildAsync(new(manifestPath));
                    Assert.False(changed.success);
                    Assert.Equal(nativeBefore, await File.ReadAllBytesAsync(built.nativeArtifact!));
                }
                finally { await File.WriteAllBytesAsync(canonical, originalCanonical); }
            }
            Dictionary<string, string> before = Snapshot(previous.outputRoot);
            Assert.NotEmpty(before);

            // Return a valid same-sized but incorrect detached alias graph and
            // require independent proof. The real compiler must reject those
            // facts even on entry points that do not request native linking.
            state.corruptFacts = true;
            if (entryPoint == "build-prebuilt")
            {
                await File.WriteAllBytesAsync(Path.Combine(root, "unverified-native.bin"), [0, 1, 2, 3]);
                configuration = configuration with
                {
                    nativeBuild = new() { mode = "prebuilt", artifacts = ["unverified-native.bin"],
                        prebuiltTargetId = Assert.Single(previous.targetModels).targetId }
                };
            }
            else if (entryPoint == "build-sources")
                configuration = configuration with { nativeBuild = new() { mode = "sources", provider = "clang" } };
            GenerationRequest request = new(configuration, root);
            GenerationResult rejected = entryPoint switch
            {
                "generate" => await host.GenerateAsync(request),
                "bridge" => await host.BridgeAsync(request),
                _ => (await host.BuildAsync(new(request))).generation
            };
            Assert.False(rejected.success);
            Assert.False(rejected.published);
            Assert.Empty(rejected.artifacts);
            Assert.Contains("Independent parser-adapted native fact compilation failed", rejected.GetDiagnosticSummary());
            Assert.Equal(before.OrderBy(item => item.Key), Snapshot(previous.outputRoot).OrderBy(item => item.Key));
            Assert.Empty(Directory.GetDirectories(root, ".bgcs-staging-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task UnsupportedCProofRequirementCannotDisappearWhenNoNativeHelperWasRequested()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-required-c-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int answer(void);\n");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1,
                library = new() { id = "c-proof-barrier", nativeName = "c_proof_barrier" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false },
                cache = new() { enabled = false }
            };
            State state = new();
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns()
                .WithFrontendFactory(() => new ProofFrontend(WorkerRoot(), state)).Build();
            GenerationRequest request = new(configuration, root);
            GenerationResult previous = await host.GenerateAsync(request);
            Assert.True(previous.success, previous.GetDiagnosticSummary());
            Dictionary<string, string> before = Snapshot(previous.outputRoot);
            state.requireCProof = true;
            GenerationResult rejected = await host.GenerateAsync(request);
            Assert.False(rejected.success);
            Assert.False(rejected.published);
            Assert.Contains("Independent parser-adaptation proofs currently require C++ source", rejected.GetDiagnosticSummary());
            Assert.Equal(before.OrderBy(item => item.Key), Snapshot(previous.outputRoot).OrderBy(item => item.Key));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class State
    {
        internal bool corruptFacts;
        internal bool requireCProof;
    }

    private sealed class ProofFrontend(string workerRoot, State state) : IFrontend
    {
        public async ValueTask<FrontendResult> ParseAsync(FrontendRequest request, CancellationToken cancellationToken)
        {
            FrontendResult result = await new FrontendWorkerClient(Path.Combine(workerRoot, "BGCS.Frontend.Clang.Worker.dll"))
                .ParseAsync(request, cancellationToken);
            if (request.language != SourceLanguage.Cpp)
                return state.requireCProof ? result with { requiresNativeCompilerProof = true } : result;
            if (!state.corruptFacts) return result with { requiresNativeCompilerProof = true };
            NativeModule original = result.module;
            NativeModule changed = new(original.targetId, original.triple, original.pointerSize,
                original.types.Select(type => type.category == NativeTypeCategory.Primitive && type.primitiveKind == "Int"
                    ? type with { spelling = "float", primitiveKind = "Float", isSigned = null } : type).ToImmutableArray(),
                original.declarations, original.diagnostics)
                { language = original.language, byteOrder = original.byteOrder };
            return result with { module = changed, requiresNativeCompilerProof = true };
        }
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
