using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class ManifestNativePluginTests
{
    [Theory]
    [InlineData("file")]
    [InlineData("directory")]
    [InlineData("missing")]
    public async Task PortablePluginActivatesInFreshNativeHostAndItsOriginalExternalInputsRemainLocked(string changedInput)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs native portable plugin " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string pluginRoot = Path.Combine(temporary, "plugins");
            Directory.CreateDirectory(pluginRoot);
            byte[] implementation = CompilePlugin();
            await File.WriteAllBytesAsync(Path.Combine(pluginRoot, "PortableNativeGuard.dll"), implementation);
            PluginManifest plugin = new()
            {
                schemaVersion = 1, assembly = "PortableNativeGuard.dll", entryType = "PortableNativeGuard",
                plugin = new("portable.native-guard", "1.0.0", [new("portable.native-guard/validator", ExtensionPort.GenerationValidator)]),
                assets = [new("PortableNativeGuard.dll", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(implementation))))]
            };
            await File.WriteAllTextAsync(Path.Combine(pluginRoot, "plugin.json"), JsonSerializer.Serialize(plugin,
                new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }));
            await File.WriteAllTextAsync(Path.Combine(temporary, "policy.txt"), "original policy");
            Directory.CreateDirectory(Path.Combine(temporary, "policy-directory"));
            await File.WriteAllTextAsync(Path.Combine(temporary, "lifecycle.txt"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.h"), "int twice(int value);\n");
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.c"), "#if defined(_WIN32)\n__declspec(dllexport)\n#endif\nint twice(int value) { return value * 2; }\n");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1,
                library = new() { id = "portable", nativeName = "portable_native" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false },
                extensions = new() { plugins = ["plugins/plugin.json"] }, runtime = new() { mode = "source" }, cache = new() { enabled = false },
                nativeBuild = new() { mode = "sources", sources = ["native.c"] }
            };
            string output;
            string manifest;
            await using (BindingHost generationHost = CreateHost())
            {
                GenerationResult generated = await generationHost.GenerateAsync(new(configuration, temporary));
                Assert.True(generated.success, generated.GetDiagnosticSummary());
                output = generated.outputRoot;
                manifest = Assert.Single(Directory.GetFiles(output, "bridge.manifest.json", SearchOption.AllDirectories));
            }
            Assert.Equal(new[] { "configure", "dispose" }, await File.ReadAllLinesAsync(Path.Combine(temporary, "lifecycle.txt")));
            await using BindingHost nativeHost = CreateHost();
            ManifestNativeBuildResult built = await nativeHost.NativeBuildAsync(new(manifest));
            Assert.True(built.success, built.GetDiagnosticSummary());
            Assert.Equal(new[] { "configure", "dispose", "configure", "native-validation", "dispose" }, await File.ReadAllLinesAsync(Path.Combine(temporary, "lifecycle.txt")));
            var previous = Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(output, path), File.ReadAllBytes);
            if (changedInput == "file") await File.AppendAllTextAsync(Path.Combine(temporary, "policy.txt"), "changed");
            else if (changedInput == "directory") await File.WriteAllTextAsync(Path.Combine(temporary, "policy-directory", "new-policy.txt"), "new");
            else await File.WriteAllTextAsync(Path.Combine(temporary, "missing-policy.txt"), "appeared");
            ManifestNativeBuildResult rejected = await nativeHost.NativeBuildAsync(new(manifest));
            Assert.False(rejected.success);
            Assert.Contains("extension input changed after manifest generation", rejected.GetDiagnosticSummary());
            Assert.Equal(5, (await File.ReadAllLinesAsync(Path.Combine(temporary, "lifecycle.txt"))).Length);
            Assert.Equal(previous.Keys.Order(), Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(output, path)).Order());
            foreach ((string path, byte[] bytes) in previous) Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(output, path)));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static byte[] CompilePlugin()
    {
        const string source = """
            using System;
            using System.Collections.Immutable;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using BGCS.Contracts;
            using BGCS.Contracts.Inputs;
            using BGCS.Contracts.Plugins;
            using BGCS.Contracts.Ports;
            using BGCS.Contracts.Requests;
            public sealed class PortableNativeGuard : IBindingPlugin, IExternalInputProvider, IAsyncDisposable
            {
                private string? log;
                public PluginDescriptor descriptor => new("portable.native-guard", "1.0.0", [new("portable.native-guard/validator", ExtensionPort.GenerationValidator)]);
                public ImmutableArray<DeclaredExternalInput> GetExternalInputs(ExternalInputContext context, CancellationToken token)
                    => [new(Path.Combine(context.pathBase, "policy.txt"), ExternalInputKind.File),
                        new(Path.Combine(context.pathBase, "policy-directory"), ExternalInputKind.Directory),
                        new(Path.Combine(context.pathBase, "missing-policy.txt"), ExternalInputKind.File, true)];
                public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration settings)
                {
                    string lifecycle = Path.Combine(settings.defaultPathBase, "lifecycle.txt");
                    log = lifecycle;
                    File.AppendAllText(lifecycle, "configure\n");
                    registry.RegisterFactory<IGenerationValidator>("portable.native-guard/validator", () => new Gate(lifecycle));
                }
                public void Dispose() => throw new InvalidOperationException("Synchronous plugin disposal must not replace async cleanup.");
                public ValueTask DisposeAsync() { if (log is not null) File.AppendAllText(log, "dispose\n"); return ValueTask.CompletedTask; }
                private sealed class Gate(string log) : IGenerationValidator
                {
                    public string id => "portable.native-guard/validator";
                    public int priority => 0;
                    public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken token)
                    {
                        if (request.nativeLinked)
                        {
                            if (request.managedCompiled || !request.nativeArtifactVerified || request.interop.functions.IsEmpty)
                                throw new InvalidOperationException("Native-only evidence or locked ABI was incorrect.");
                            File.AppendAllText(log, "native-validation\n");
                        }
                        return ValueTask.FromResult(ImmutableArray<BindingDiagnostic>.Empty);
                    }
                }
            }
            """;
        string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = platform.Where(path => Path.GetDirectoryName(path) == runtime).Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(IBindingPlugin).Assembly.Location));
        CSharpCompilation compilation = CSharpCompilation.Create("PortableNativeGuard", [CSharpSyntaxTree.ParseText(source, new(LanguageVersion.CSharp14))], references,
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return output.ToArray();
    }
    private static BindingHost CreateHost() => BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
