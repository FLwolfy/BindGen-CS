using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeConstantDriftTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeOnlyAliasRetargetAndExecutionCharsetChangesCannotPublishStaleConstants(bool stringCharset)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs native constant drift " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = """
                __declspec(dllexport) int first(void);
                __declspec(dllexport) int second(void);
                #ifdef RETARGET_ALIAS
                #define SELECTED_ALIAS (second)
                #else
                #define SELECTED_ALIAS (first)
                #endif
                """ + (stringCharset ? "\n#define GREETING \"é\"\n#define EXACT_UTF8 u8\"A\\0\\U0001F600\"\n" : string.Empty);
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), source);
            await File.WriteAllTextAsync(Path.Combine(root, "native.c"), "#include \"native.h\"\nint first(void) { return 42; }\nint second(void) { return 17; }\n");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "constant_drift", nativeName = "constant_drift" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], includeTransitivelyReferencedHeaders = false },
                runtime = new() { mode = "source" }, nativeBuild = new() { mode = "sources", provider = "msvc", sources = ["native.c"] },
                cache = new() { enabled = true }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult positive = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(positive.success, positive.GetDiagnosticSummary());
            if (stringCharset)
            {
                Assert.Equal(2, Assert.Single(positive.generation.targetModels).interop.stringConstants.Length);
                string targetRoot = Path.Combine(positive.outputRoot, Assert.Single(positive.generation.targetModels).targetId);
                using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(targetRoot, "metadata", "verification.json")));
                JsonElement receipt = Assert.Single(document.RootElement.GetProperty("nativeEvidence").GetProperty("construction").GetProperty("literalProofs").EnumerateArray());
                Assert.Equal(0, receipt.GetProperty("execution").GetProperty("exitCode").GetInt32());
                Assert.False(receipt.GetProperty("execution").GetProperty("timedOut").GetBoolean());
                Assert.Equal(2, receipt.GetProperty("verifiedConstants").GetArrayLength());
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(receipt.GetProperty("source").GetString()!))),
                    receipt.GetProperty("sourceDigest").GetProperty("value").GetString());
                Assert.Contains("cl.exe", receipt.GetProperty("command").GetProperty("executable").GetString());
                ManifestNativeBuildResult standalone = await host.NativeBuildAsync(new(Path.Combine(targetRoot, "native", "metadata", "bridge.manifest.json")));
                Assert.True(standalone.success, standalone.GetDiagnosticSummary());
                var standaloneProof = Assert.Single(standalone.literalProofs);
                Assert.Equal(0, standaloneProof.execution.exitCode);
                Assert.Equal(2, standaloneProof.verifiedConstants.Length);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(standaloneProof.source))), standaloneProof.sourceDigest.value);
            }
            else Assert.Single(Assert.Single(positive.generation.targetModels).interop.functionAliases);
            ImmutableDictionary<string, string> previous = Snapshot(positive.outputRoot);
            BindingConfiguration changed = configuration with { nativeBuild = configuration.nativeBuild with
                { compilerArguments = [stringCharset ? "/execution-charset:windows-1252" : "/DRETARGET_ALIAS=1"] } };
            BuildResult rejected = await host.BuildAsync(new(new(changed, root)));
            Assert.False(rejected.success);
            string summary = rejected.GetDiagnosticSummary();
            Assert.True(summary.Contains(stringCharset ? "Native execution-character string verification failed" : "exact function alias target identity", StringComparison.Ordinal), summary);
            Assert.Equal(previous.OrderBy(pair => pair.Key), Snapshot(positive.outputRoot).OrderBy(pair => pair.Key));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ImmutableDictionary<string, string> Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .ToImmutableDictionary(path => Path.GetRelativePath(directory, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
