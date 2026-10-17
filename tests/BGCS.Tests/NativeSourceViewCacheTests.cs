using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeSourceViewCacheTests
{
    [Fact]
    public async Task GnuRecordTraitsAreReacquiredBeforeWarmReuseAndRemainStableWithDebugObjects()
    {
        if (OperatingSystem.IsWindows() || !OperatingSystem.IsLinux()
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BGCS_GNU_CXX")))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("BGCS_REQUIRE_GNU_SOURCE_VIEW"));
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("BGCS_REQUIRE_GNU"));
            return;
        }
        NativeTargetDescriptor compiler = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new NativeTargetId("host"))), SourceLanguage.Cpp, "gnu");
        Assert.NotNull(compiler.toolchain.compilerPath);
        string root = Path.Combine(Path.GetTempPath(), "bgcs gnu trait cache " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string original = """
                #include <memory>
                namespace trait_cache {
                class Holder {
                    std::unique_ptr<int> m_value;
                public:
                    Holder() = default;
                    ~Holder() noexcept = default;
                    int Read() const { return m_value ? *m_value : 42; }
                };
                }
                """;
            string header = Path.Combine(root, "native.hpp");
            await File.WriteAllTextAsync(header, original);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1,
                library = new() { id = "gnu_trait_cache", nativeName = "gnu_trait_cache" },
                source = new() { language = SourceLanguage.Cpp, languageStandard = "c++23", headers = ["native.hpp"], parseMacros = false, arguments = ["-g"] },
                targets = [new() { settings = new() { compilerPath = compiler.toolchain.compilerPath } }],
                nativeBuild = new() { provider = "gnu", mode = "none" },
                cache = new() { enabled = true },
                api = new() { @namespace = "Generated.GnuTraitCache", name = "TraitApi" }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(PrepareWorker(root)).Build();
            GenerationRequest request = new(configuration, root);
            GenerationResult first = await host.GenerateAsync(request);
            Assert.True(first.success, first.GetDiagnosticSummary());
            ExpectStage(first, "primaryFrontend", "miss");
            ExpectStage(first, "csharpEmission", "miss");
            await AssertTraits(first, true);
            string managed = Managed(first);
            var generatedSources = first.artifacts.Where(artifact => artifact.mediaType.StartsWith("text/", StringComparison.Ordinal))
                .ToDictionary(artifact => artifact.relativePath, artifact => File.ReadAllBytes(Path.Combine(first.outputRoot, artifact.relativePath)));
            GenerationResult warm = await host.GenerateAsync(request);
            Assert.True(warm.success, warm.GetDiagnosticSummary());
            ExpectStage(warm, "primaryFrontend", "hit");
            ExpectStage(warm, "csharpEmission", "hit");
            await AssertTraits(warm, true);
            Assert.Equal(managed, Managed(warm));
            foreach ((string path, byte[] bytes) in generatedSources) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(warm.outputRoot, path)));
            string verificationPath = Path.Combine(warm.outputRoot, Assert.Single(warm.targetModels).targetId, "metadata", "verification.json");
            string warmVerification = await File.ReadAllTextAsync(verificationPath);
            GenerationResult repeated = await host.GenerateAsync(request);
            Assert.True(repeated.success, repeated.GetDiagnosticSummary());
            ExpectStage(repeated, "primaryFrontend", "hit");
            ExpectStage(repeated, "csharpEmission", "hit");
            await AssertTraits(repeated, true);
            Assert.Equal(managed, Managed(repeated));
            string receiptComparison = ReceiptDifferences(warmVerification, await File.ReadAllTextAsync(verificationPath));
            Assert.True(receiptComparison == "No non-cache field differences.", "Consecutive warm verification fields (only top-level cache omitted):\n" + receiptComparison);
            var before = Directory.GetFiles(warm.outputRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            GenerationDiffResult unchanged = await host.DiffAsync(request);
            Assert.True(unchanged.success, string.Join(Environment.NewLine, unchanged.diagnostics.Select(diagnostic => diagnostic.message)));
            Assert.True(unchanged.changes.IsEmpty, "Consecutive warm verification fields (only top-level cache omitted):\n" + receiptComparison
                + "\nDiff changes: " + string.Join("; ", unchanged.changes.Select(change => change.ToString()))
                + "\nDiff diagnostics: " + string.Join(Environment.NewLine, unchanged.diagnostics.Select(diagnostic => diagnostic.message)));
            Assert.Empty(unchanged.changes);
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(warm.outputRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
            foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));

            string changedSource = original.Replace("~Holder()", "virtual ~Holder()", StringComparison.Ordinal);
            Assert.NotEqual(original, changedSource);
            await File.WriteAllTextAsync(header, changedSource);
            GenerationResult changed = await host.GenerateAsync(request);
            Assert.True(changed.success, changed.GetDiagnosticSummary());
            ExpectStage(changed, "primaryFrontend", "miss");
            ExpectStage(changed, "csharpEmission", "miss");
            await AssertTraits(changed, false);

            static string ReceiptDifferences(string previousJson, string currentJson)
            {
                using JsonDocument previousDocument = JsonDocument.Parse(previousJson), currentDocument = JsonDocument.Parse(currentJson);
                List<string> differences = [];
                Compare(previousDocument.RootElement, currentDocument.RootElement, "$");
                return differences.Count == 0 ? "No non-cache field differences." : string.Join(Environment.NewLine, differences);

                void Compare(JsonElement left, JsonElement right, string path)
                {
                    if (differences.Count == 12) return;
                    if (left.ValueKind != right.ValueKind) { Add(path, Short(left), Short(right)); return; }
                    if (left.ValueKind == JsonValueKind.Object)
                    {
                        var oldProperties = left.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
                        var newProperties = right.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
                        foreach (string name in oldProperties.Keys.Union(newProperties.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                        {
                            if (differences.Count == 12) break;
                            if (path == "$" && name == "cache") continue;
                            string child = path + "[" + JsonSerializer.Serialize(name) + "]";
                            if (!oldProperties.TryGetValue(name, out JsonElement oldValue)) Add(child, "<missing>", Short(newProperties[name]));
                            else if (!newProperties.TryGetValue(name, out JsonElement newValue)) Add(child, Short(oldValue), "<missing>");
                            else Compare(oldValue, newValue, child);
                        }
                    }
                    else if (left.ValueKind == JsonValueKind.Array)
                    {
                        if (left.GetArrayLength() != right.GetArrayLength())
                            Add(path + ".Length", left.GetArrayLength().ToString(), right.GetArrayLength().ToString());
                        for (int index = 0; index < Math.Min(left.GetArrayLength(), right.GetArrayLength()) && differences.Count < 12; index++)
                            Compare(left[index], right[index], path + "[" + index + "]");
                    }
                    else if (!JsonElement.DeepEquals(left, right)) Add(path, Short(left), Short(right));
                }
                void Add(string path, string left, string right)
                {
                    if (differences.Count < 12) differences.Add(path + ": " + left + " -> " + right);
                }
                static string Short(JsonElement value)
                {
                    string text = value.GetRawText();
                    return text.Length <= 180 ? text : text[..180] + "...";
                }
            }

            async Task AssertTraits(
                GenerationResult result,
                bool standardLayout
            ) {
                TargetInspection target = Assert.Single(result.targetModels);
                Assert.Equal(compiler.toolchain.compilerPath, target.resolvedTarget!.toolchain.compilerPath);
                var holder = Assert.Single(target.native.declarations, declaration => declaration.kind == "Class" && declaration.qualifiedName == "trait_cache::Holder");
                Assert.Equal(standardLayout, holder.isStandardLayout);
                Assert.True(holder.isDefaultConstructible);
                Assert.False(holder.isTriviallyCopyable);
                string targetRoot = Path.Combine(result.outputRoot, target.targetId);
                string assertions = await File.ReadAllTextAsync(Path.Combine(targetRoot, "native", "src", "storage.assertions.cpp"));
                Assert.Contains("trait_cache::Holder>::value == " + (standardLayout ? "1" : "0"), assertions);
                using JsonDocument verification = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(targetRoot, "metadata", "verification.json")));
                Assert.True(verification.RootElement.GetProperty("nativeFactProofRequired").GetBoolean());
                JsonElement proof = verification.RootElement.GetProperty("nativeFactProof");
                Assert.True(proof.GetProperty("compiled").GetBoolean());
                Assert.False(proof.GetProperty("finalLinked").GetBoolean());
                Assert.Contains(proof.GetProperty("sourceIdentity").GetProperty("arguments").EnumerateArray(), argument => argument.GetString() == "-g");
                JsonElement[] observations = verification.RootElement.GetProperty("nativeSourceInputs").EnumerateArray()
                    .Where(observation => observation.TryGetProperty("kind", out JsonElement kind) && kind.GetString() == "nativeRecordTraits").ToArray();
                Assert.NotEmpty(observations);
                Assert.Contains(observations.SelectMany(observation => observation.GetProperty("facts").EnumerateArray()), fact =>
                    fact.GetProperty("declaration").GetProperty("value").GetString() == holder.id.value
                    && fact.GetProperty("isStandardLayout").GetBoolean() == standardLayout
                    && fact.GetProperty("isDefaultConstructible").GetBoolean()
                    && !fact.GetProperty("isTriviallyCopyable").GetBoolean());
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("clang")]
    [InlineData("msvc")]
    [InlineData("gnu")]
    public async Task NativePreprocessingStillObservesAbsoluteMissingHeadersBeforeDetachedFactReuse(string family)
    {
        if (!Available(family)) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs native view cache " + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source"), external = Path.Combine(root, "external-query");
        Directory.CreateDirectory(source); Directory.CreateDirectory(external);
        try
        {
            string appeared = Path.Combine(external, "feature.h");
            string literal = appeared.Replace('\\', '/');
            await File.WriteAllTextAsync(Path.Combine(source, "native.h"), $$"""
                #if __has_include("{{literal}}")
                double echo(double value);
                #else
                int echo(int value);
                #endif
                """);
            BindingConfiguration configuration = Configuration(family);
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(PrepareWorker(root)).Build();
            GenerationRequest request = new(configuration, source);
            GenerationResult first = await host.GenerateAsync(request);
            Assert.True(first.success, first.GetDiagnosticSummary());
            ExpectStage(first, "primaryFrontend", "miss");
            GenerationResult warm = await host.GenerateAsync(request);
            Assert.True(warm.success, warm.GetDiagnosticSummary());
            ExpectStage(warm, "primaryFrontend", "hit");
            ExpectStage(warm, "csharpEmission", "hit");
            Assert.Contains("int", Managed(warm));
            await File.WriteAllTextAsync(appeared, "/* native absolute header observation changes */\n");
            GenerationResult changed = await host.GenerateAsync(request);
            Assert.True(changed.success, changed.GetDiagnosticSummary());
            ExpectStage(changed, "primaryFrontend", "miss");
            ExpectStage(changed, "csharpEmission", "miss");
            Assert.Contains("double", Managed(changed));
            GenerationResult stable = await host.GenerateAsync(request);
            Assert.True(stable.success, stable.GetDiagnosticSummary());
            ExpectStage(stable, "primaryFrontend", "hit");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task WarmFactsAndEmissionDoNotSkipRequestedNativeConstructionOrChangedNativeSourceBytes()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs native view build cache " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                API int answer(void);
                """);
            string implementation = Path.Combine(root, "native.c");
            await File.WriteAllTextAsync(implementation, "#include \"native.h\"\nAPI int answer(void) { return 42; }\n");
            BindingConfiguration configuration = Configuration("clang") with { nativeBuild = new() { mode = "sources", provider = "clang", sources = ["native.c"] } };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(PrepareWorker(root)).Build();
            BuildRequest request = new(new(configuration, root));
            BuildResult first = await host.BuildAsync(request);
            Assert.True(first.success, first.GetDiagnosticSummary());
            string Binary(BuildResult result) => Assert.Single(Directory.GetFiles(Path.Combine(result.outputRoot,
                Assert.Single(result.generation.targetModels).targetId, "native", "bin")));
            Assert.Equal(42, Invoke(Binary(first)));
            await File.WriteAllTextAsync(implementation, "#include \"native.h\"\nAPI int answer(void) { return 43; }\n");
            BuildResult second = await host.BuildAsync(request);
            Assert.True(second.success, second.GetDiagnosticSummary());
            ExpectStage(second.generation, "primaryFrontend", "hit");
            ExpectStage(second.generation, "csharpEmission", "hit");
            Assert.Equal(43, Invoke(Binary(second)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CppBridgeReparseUsesLogicalGeneratedInputsForWarmEmissionAndUnchangedDiff()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs cpp native view cache " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.hpp");
            await File.WriteAllTextAsync(header, "namespace bridge_cache { inline int echo(int value) { return value; } }\n");
            BindingConfiguration configuration = Configuration("clang");
            configuration = configuration with
            {
                source = configuration.source with { language = SourceLanguage.Cpp, languageStandard = "c++23", headers = ["native.hpp"] }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(PrepareWorker(root)).Build();
            GenerationRequest request = new(configuration, root);
            GenerationResult first = await host.GenerateAsync(request);
            Assert.True(first.success, first.GetDiagnosticSummary());
            ExpectStage(first, "primaryFrontend", "miss");
            ExpectStage(first, "csharpEmission", "miss");
            GenerationResult warm = await host.GenerateAsync(request);
            Assert.True(warm.success, warm.GetDiagnosticSummary());
            ExpectStage(warm, "primaryFrontend", "hit");
            ExpectStage(warm, "csharpEmission", "hit");
            Assert.NotNull(Assert.Single(warm.targetModels).bridge);
            var before = Directory.GetFiles(warm.outputRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            GenerationDiffResult unchanged = await host.DiffAsync(request);
            Assert.True(unchanged.success, string.Join(Environment.NewLine, unchanged.diagnostics.Select(diagnostic => diagnostic.message)));
            Assert.Empty(unchanged.changes);
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(warm.outputRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
            foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
            await File.WriteAllTextAsync(header, "namespace bridge_cache { inline double echo(double value) { return value; } }\n");
            GenerationResult changed = await host.GenerateAsync(request);
            Assert.True(changed.success, changed.GetDiagnosticSummary());
            ExpectStage(changed, "primaryFrontend", "miss");
            ExpectStage(changed, "csharpEmission", "miss");
            Assert.Contains("double", Managed(changed));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static int Invoke(string binary)
    {
        nint handle = NativeLibrary.Load(binary);
        try
        {
            Answer call = Marshal.GetDelegateForFunctionPointer<Answer>(NativeLibrary.GetExport(handle, "answer"));
            int answer = call(); GC.KeepAlive(call); return answer;
        }
        finally { NativeLibrary.Free(handle); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Answer();
    private static GenerationCacheStageResult Stage(GenerationResult result, string stage) => Assert.Single(result.cacheStages, item => item.stage == stage);
    private static void ExpectStage(GenerationResult result, string name, string expected)
    {
        GenerationCacheStageResult stage = Stage(result, name);
        Assert.True(stage.status == expected, name + " expected " + expected + ", actual " + stage.status + ": " + stage.reason);
    }
    private static string Managed(GenerationResult result) => string.Join("\n", result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
        .Select(artifact => File.ReadAllText(Path.Combine(result.outputRoot, artifact.relativePath))));
    private static BindingConfiguration Configuration(string family)
    {
        NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new NativeTargetId("host"))), SourceLanguage.C, family);
        Assert.NotNull(target.toolchain.compilerPath);
        return new()
        {
            schemaVersion = 1, library = new() { id = "native_view_cache", nativeName = "native_view_cache" },
            source = new() { headers = ["native.h"], parseMacros = false },
            targets = [new() { settings = new() { compilerPath = target.toolchain.compilerPath } }],
            nativeBuild = new() { provider = family },
            api = new() { @namespace = "Generated.NativeViewCache", name = "CacheApi" }
        };
    }
    private static bool Available(string family) => (family != "msvc" || OperatingSystem.IsWindows())
        && (family != "gnu" || !OperatingSystem.IsWindows() && (OperatingSystem.IsLinux()
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BGCS_GNU_CC"))));
    private static string PrepareWorker(string root)
    {
        string compiled = WorkerRoot(), worker = Path.Combine(root, "worker");
        Directory.CreateDirectory(worker);
        foreach (string file in Directory.GetFiles(compiled).Where(file => Path.GetFileName(file) != "worker-assets.manifest.json"))
            File.Copy(file, Path.Combine(worker, Path.GetFileName(file)));
        string native = Path.Combine("runtimes", RuntimeInformation.RuntimeIdentifier, "native");
        Directory.CreateDirectory(Path.Combine(worker, native));
        foreach (string file in Directory.GetFiles(Path.Combine(compiled, native)))
            File.Copy(file, Path.Combine(worker, native, Path.GetFileName(file)));
        WorkerAssetManifest manifest = new(1, RuntimeInformation.RuntimeIdentifier, WorkerProtocol.C_PARSER_IDENTITY,
            Directory.GetFiles(worker, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(file =>
            {
                using FileStream stream = File.OpenRead(file);
                return new ArtifactDescriptor(Path.GetRelativePath(worker, file).Replace('\\', '/'), "application/octet-stream",
                    new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))));
            }).ToImmutableArray());
        File.WriteAllText(Path.Combine(worker, "worker-assets.manifest.json"), JsonSerializer.Serialize(manifest));
        new WorkerAssetDeployer().VerifyDeployment(worker, manifest, RuntimeInformation.RuntimeIdentifier);
        return worker;
    }
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
