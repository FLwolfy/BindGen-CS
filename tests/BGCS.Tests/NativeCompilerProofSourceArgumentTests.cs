using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCompilerProofSourceArgumentTests
{
    [Fact]
    public async Task OriginalProofKeepsExpandedSourceOrderAndForcedIncludesSeparateFromFinalBuildFlags()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-proof-source-argv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string forced = Path.Combine(root, "forced.hpp");
            await File.WriteAllTextAsync(forced, """
                #if BGCS_ORDER != 2 || BGCS_SOURCE_ONLY != 1
                #error The original source arguments were dropped, reordered or applied twice
                #endif
                #ifdef BGCS_REMOVED
                #error The original undefine was not preserved
                #endif
                #ifdef BGCS_FINAL_BUILD_ONLY
                #error Final native-build arguments leaked into the source-identity proof
                #endif
                #define BGCS_FORCED_INCLUDE 1
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "native.hpp"), """
                #if !defined(BGCS_FORCED_INCLUDE) || BGCS_ORDER != 2
                #error The independent proof omitted the original forced include or final definition
                #endif
                namespace SourceProof { using Number = int; inline Number answer() { return 42; } }
                """);
            var target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, "clang");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1,
                library = new() { id = "source-proof", nativeName = "source_proof" },
                source = new()
                {
                    language = SourceLanguage.Cpp, headers = ["native.hpp"], parseMacros = false,
                    arguments = ["-UBGCS_ORDER", "-DBGCS_ORDER=1", "-UBGCS_ORDER", "-DBGCS_ORDER=2", "-DBGCS_SOURCE_ONLY=1",
                        "-DBGCS_REMOVED=1", "-UBGCS_REMOVED", "-include", forced]
                },
                targets = [new() { settings = new() { compilerPath = target.toolchain.compilerPath, arguments = ["-DBGCS_ORDER=0"] } }],
                nativeBuild = new() { provider = "clang", compilerArguments = ["-DBGCS_FINAL_BUILD_ONLY=1"] },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns()
                .WithFrontendFactory(() => new ProofFrontend(WorkerRoot())).Build();
            GenerationResult generated = await host.GenerateAsync(new(configuration, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            string targetId = Assert.Single(generated.targetModels).targetId;
            string receiptPath = Path.Combine(generated.outputRoot, targetId, "metadata", "verification.json");
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(receiptPath));
            JsonElement receipt = document.RootElement.GetProperty("nativeFactProof");
            Assert.True(receipt.GetProperty("compiled").GetBoolean());
            Assert.False(receipt.GetProperty("finalLinked").GetBoolean());
            string[] arguments = receipt.GetProperty("sourceIdentity").GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!).ToArray();
            Assert.Equal(1, arguments.Count(argument => argument == "-DBGCS_ORDER=0"));
            Assert.Equal(1, arguments.Count(argument => argument == "-DBGCS_ORDER=2"));
            Assert.True(Array.IndexOf(arguments, "-DBGCS_ORDER=0") < Array.IndexOf(arguments, "-DBGCS_ORDER=2"));
            Assert.Contains("-include", arguments);
            Assert.Contains(forced, arguments);
            Assert.DoesNotContain(arguments, argument => argument.Contains("BGCS_FINAL_BUILD_ONLY", StringComparison.Ordinal));
            Assert.DoesNotContain("bgcs-native-fact-proof-", receipt.GetRawText(), StringComparison.Ordinal);
            // Reusable external locks and artifact read locks remain distinct.
            // Destroyed scratch queries cannot become manifest filesystem URIs.
            JsonElement[] snapshots = receipt.GetProperty("inputs").EnumerateArray().ToArray();
            string nativeSourceRoot = "artifact:/" + targetId + "/native/src";
            Assert.All(snapshots, snapshot => Assert.DoesNotContain(snapshot.GetProperty("searchDirectories").EnumerateArray(),
                directory => directory.GetProperty("path").GetString()!.StartsWith("artifact:/", StringComparison.Ordinal)));
            Assert.All(snapshots, snapshot => Assert.DoesNotContain(snapshot.GetProperty("excludedDirectories").EnumerateArray(),
                directory => directory.GetString()!.StartsWith("native-proof:/", StringComparison.Ordinal)));
            Assert.Contains(snapshots.SelectMany(snapshot => snapshot.GetProperty("files").EnumerateArray()), file => file.GetProperty("path").GetString() == forced);
            Assert.Contains(receipt.GetProperty("generatedInputs").EnumerateArray(), file =>
                file.GetProperty("path").GetString() == nativeSourceRoot + "/canonical.assertions.cpp"
                && file.GetProperty("length").GetInt64() > 0 && file.GetProperty("digest").GetProperty("value").GetString()!.Length == 64);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class ProofFrontend(string workerRoot) : IFrontend
    {
        public async ValueTask<FrontendResult> ParseAsync(FrontendRequest request, CancellationToken cancellationToken)
        {
            FrontendResult result = await new FrontendWorkerClient(Path.Combine(workerRoot, "BGCS.Frontend.Clang.Worker.dll"))
                .ParseAsync(request, cancellationToken);
            return result with { requiresNativeCompilerProof = request.language == SourceLanguage.Cpp };
        }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
