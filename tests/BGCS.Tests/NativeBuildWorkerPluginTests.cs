using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeBuildWorkerPluginTests
{
    [Fact]
    public async Task NativeCandidatesAndStandaloneManifestRecaptureIsolatedContributorsAndRejectActivationDrift()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs native worker contributor " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string pluginRoot = Path.Combine(root, "plugin");
            Directory.CreateDirectory(pluginRoot);
            string fixture = Path.Combine(Repository(), "tests", "fixtures", "plugins", "ClangFactPlugin", "bin", Configuration(), "net10.0");
            string[] names = ["ClangFactPlugin.dll", "ClangFactPlugin.deps.json"];
            foreach (string name in names) File.Copy(Path.Combine(fixture, name), Path.Combine(pluginRoot, name));
            PluginManifest plugin = new()
            {
                schemaVersion = 1, assembly = names[0], entryType = "ClangFactFixture.FingerprintedFactPlugin",
                plugin = new("clang.fixture", "1.0.0", [new("clang.fixture.facts", ExtensionPort.FrontendFactContributor)])
                {
                    configurationSchema = JsonSerializer.SerializeToElement(new
                    {
                        type = "object", additionalProperties = false, required = new[] { "externalFile" },
                        properties = new { externalFile = new { type = "string", minLength = 1 }, mode = new { type = "string" } }
                    })
                },
                workerOnly = true, workerParserIdentity = WorkerProtocol.C_PARSER_IDENTITY,
                assets = names.Select(name => new PluginAsset(name, Digest(File.ReadAllBytes(Path.Combine(pluginRoot, name))))).ToImmutableArray()
            };
            await File.WriteAllTextAsync(Path.Combine(pluginRoot, "plugin.json"), JsonSerializer.Serialize(plugin,
                new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }));
            await File.WriteAllTextAsync(Path.Combine(pluginRoot, "external.txt"), "native-proof");
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int read_value(void);\n");
            await File.WriteAllTextAsync(Path.Combine(root, "native.c"), "#if defined(_WIN32)\n__declspec(dllexport)\n#endif\nint read_value(void) { return 42; }\n");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "worker-native", nativeName = "worker_native" },
                source = new() { headers = ["native.h"], parseMacros = false },
                runtime = new() { mode = "source" }, cache = new() { enabled = false },
                extensions = new()
                {
                    plugins = ["plugin/plugin.json"],
                    options = ImmutableDictionary<string, PluginSettingsConfiguration>.Empty.Add("clang.fixture",
                        new() { values = JsonSerializer.SerializeToElement(new { externalFile = "external.txt" }), pathBase = pluginRoot })
                },
                nativeBuild = new() { mode = "sources", sources = ["native.c"] }
            };
            string output;
            await using (BindingHost host = CreateHost())
            {
                BuildResult built = await host.BuildAsync(new(new(configuration, root)));
                Assert.True(built.success, built.GetDiagnosticSummary());
                Assert.Equal("advanced-clang:read_value:native-proof", Assert.Single(Assert.Single(built.generation.targetModels).native.declarations,
                    declaration => declaration.name == "read_value").documentation);
                output = built.outputRoot;
            }
            Assert.DoesNotContain(AssemblyLoadContext.Default.Assemblies, assembly => assembly.GetName().Name == "ClangFactPlugin");
            string manifestPath = Assert.Single(Directory.GetFiles(output, "bridge.manifest.json", SearchOption.AllDirectories));
            byte[] originalManifest = await File.ReadAllBytesAsync(manifestPath);
            NativeBuildManifest manifest = JsonSerializer.Deserialize<NativeBuildManifest>(originalManifest, WorkerProtocol.jsonOptions)!;
            WorkerPluginActivation activation = Assert.Single(manifest.workerPlugins);
            Assert.NotNull(manifest.workerPluginContext);
            Assert.Equal("clang.fixture", Assert.Single(manifest.plugins).descriptor.id);
            await using BindingHost standalone = CreateHost();
            ManifestNativeBuildResult reconstructed = await standalone.NativeBuildAsync(new(manifestPath));
            Assert.True(reconstructed.success, reconstructed.GetDiagnosticSummary());
            nint module = NativeLibrary.Load(reconstructed.nativeArtifact!);
            try { Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<ReadValue>(NativeLibrary.GetExport(module, "read_value"))()); }
            finally { NativeLibrary.Free(module); }
            var before = Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            // NativeBuild writes a receipt but preserves the generation manifest.
            NativeTargetDescriptor target = manifest.workerPluginContext!.target;
            NativeTargetDescriptor different = new(target.targetId, target.platformId, target.architectureId, target.abiId,
                "unrelated-unknown-target", target.toolchain, target.capabilities, target.byteOrder);
            NativeBuildManifest[] mutations =
            [
                manifest with { workerPlugins = [], workerPluginContext = null },
                manifest with { workerPlugins = [activation, activation] },
                manifest with { workerPlugins = [activation with { parserIdentity = "different-parser" }] },
                manifest with { workerPluginContext = new(manifest.workerPluginContext.configuration, manifest.workerPluginContext.pathBase, different) },
                manifest with { workerPluginContext = new(JsonSerializer.SerializeToElement(new { changed = true }), manifest.workerPluginContext.pathBase, target) },
                manifest with { workerPlugins = [activation with { settings = JsonSerializer.SerializeToElement(new { externalFile = "different.txt" }) }] }
            ];
            foreach (NativeBuildManifest changed in mutations)
            {
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(changed, WorkerProtocol.jsonOptions));
                ManifestNativeBuildResult rejected = await standalone.NativeBuildAsync(new(manifestPath));
                Assert.False(rejected.success);
                Assert.False(rejected.published);
                Assert.Contains("worker", rejected.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
                await File.WriteAllBytesAsync(manifestPath, originalManifest);
                AssertPrevious();
            }
            string implementation = Path.Combine(pluginRoot, names[0]);
            byte[] originalImplementation = await File.ReadAllBytesAsync(implementation);
            await File.AppendAllTextAsync(implementation, "changed-executable-closure");
            ManifestNativeBuildResult changedClosure = await standalone.NativeBuildAsync(new(manifestPath));
            Assert.False(changedClosure.success);
            Assert.False(changedClosure.published);
            string closureDiagnostic = changedClosure.GetDiagnosticSummary();
            Assert.True(closureDiagnostic.Contains("plugin", StringComparison.OrdinalIgnoreCase)
                || closureDiagnostic.Contains("extension input", StringComparison.OrdinalIgnoreCase), closureDiagnostic);
            await File.WriteAllBytesAsync(implementation, originalImplementation);
            AssertPrevious();
            await File.WriteAllTextAsync(Path.Combine(pluginRoot, "external.txt"), "changed-declared-state");
            ManifestNativeBuildResult changedState = await standalone.NativeBuildAsync(new(manifestPath));
            Assert.False(changedState.success);
            Assert.False(changedState.published);
            AssertPrevious();
            Assert.DoesNotContain(AssemblyLoadContext.Default.Assemblies, assembly => assembly.GetName().Name == "ClangFactPlugin");

            void AssertPrevious()
            {
                Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(output, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
                foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadValue();
    private static ContentDigest Digest(byte[] data) => new(Convert.ToHexStringLower(SHA256.HashData(data)));
    private static BindingHost CreateHost() => BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(
        Path.Combine(Repository(), "src", "BGCS.Frontend.Clang.Worker", "bin", Configuration(), "net10.0")).Build();
    private static string Configuration() => AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
    private static string Repository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
