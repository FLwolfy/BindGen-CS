using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Workers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class ManagedConstantInputHostTests
{
    [Fact]
    public async Task FrozenSourceBytesAndDefinesChangeTheEmissionKeyAtTheSamePath()
    {
        using Scratch scratch = new();
        string source = Path.Combine(scratch.root, "values.cs");
        File.WriteAllText(source, Source(43));
        BindingConfiguration configuration = Configuration() with { api = Configuration().api with { projectionInputs = new() { sources = ["values.cs"] } } };
        using VerifiedWorker worker = new();
        await using BindingHost host = Builder(worker.root).Build();
        GenerationRequest request = new(configuration, scratch.root);
        GenerationResult cold = await host.GenerateAsync(request);
        Success(cold, "43L", "miss");
        GenerationResult warm = await host.GenerateAsync(request);
        Success(warm, "43L", "hit");
        Assert.Equal("hit", Assert.Single(warm.cacheStages, stage => stage.stage == "primaryFrontend").status);
        File.WriteAllText(source, Source(44));
        GenerationResult changed = await host.GenerateAsync(request);
        Success(changed, "44L", "miss");
        Assert.Equal("hit", Assert.Single(changed.cacheStages, stage => stage.stage == "primaryFrontend").status);
        File.WriteAllText(source, """
            namespace Vendor { public static class Values {
            #if ALTERNATE && NET10_0 && DEBUG && TRACE && NETCOREAPP3_1_OR_GREATER && NET10_0_OR_GREATER
            public const long Answer = 45L;
            #else
            #error The static projection must preserve framework and explicitly declared symbols.
            public const long Answer = 46L;
            #endif
            } }
            """);
        GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration with { api = configuration.api with { projectionInputs = configuration.api.projectionInputs with { defines = ["ALTERNATE", "DEBUG", "TRACE"] } } }, scratch.root), compileManaged: true);
        Success(built, "45L", "miss");
        string receiptPath = Assert.Single(built.artifacts, artifact => artifact.relativePath.EndsWith("/metadata/verification.json", StringComparison.Ordinal)).relativePath;
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(built.outputRoot, receiptPath)));
        JsonElement captured = receipt.RootElement.GetProperty("managedProjectionInputs");
        Assert.Contains(captured.GetProperty("files").EnumerateArray(), input => input.GetProperty("path").GetString() == source);
        Assert.DoesNotContain("\"image\"", captured.GetRawText(), StringComparison.Ordinal);
        string[] symbols = receipt.RootElement.GetProperty("managedProjectionEnvironment").GetProperty("defines").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.All(new[] { "ALTERNATE", "TRACE", "DEBUG", "NET10_0", "NET10_0_OR_GREATER" }, symbol => Assert.Contains(symbol, symbols));
    }

    [Fact]
    public async Task MetadataImagesAreNeverLoadedAndSamePathContentChangesInvalidateEmission()
    {
        using Scratch scratch = new();
        string name = "FrozenValues" + Guid.NewGuid().ToString("N");
        string reference = Path.Combine(scratch.root, name + ".dll");
        WriteAssembly(reference, name, Source(43) + """
            internal static class Poison { [System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Start() => throw new System.Exception("Consumer code must never execute during generation."); }
            """);
        BindingConfiguration configuration = Configuration() with { api = Configuration().api with { projectionInputs = new() { references = [reference] } } };
        await using BindingHost host = Builder().Build();
        GenerationRequest request = new(configuration, scratch.root);
        Success(await host.GenerateAsync(request), "43L", "miss");
        Success(await host.GenerateAsync(request), "43L", "hit");
        WriteAssembly(reference, name, Source(44));
        Success(await host.GenerateAsync(request), "44L", "miss");
        Assert.DoesNotContain(AssemblyLoadContext.All.SelectMany(context => context.Assemblies), assembly => assembly.GetName().Name == name);
    }

    [Fact]
    public async Task TransitiveMetadataAndPreviouslyAbsentHigherPriorityCandidatesAreFrozen()
    {
        using Scratch scratch = new();
        string first = Directory.CreateDirectory(Path.Combine(scratch.root, "first")).FullName;
        string fallback = Directory.CreateDirectory(Path.Combine(scratch.root, "fallback")).FullName;
        string dependencyName = "FrozenDependency" + Guid.NewGuid().ToString("N");
        string dependency = Path.Combine(fallback, dependencyName + ".dll");
        WriteAssembly(dependency, dependencyName, "namespace VendorDependency { public struct Token { public int Value; } }");
        string facadeName = "FrozenFacade" + Guid.NewGuid().ToString("N");
        string facade = Path.Combine(first, facadeName + ".dll");
        WriteAssembly(facade, facadeName, Source(43) + "namespace Vendor { public static class Unselected { public static VendorDependency.Token Token() => default; } }", dependency);
        string anchor = Path.Combine(fallback, "FrozenAnchor.dll");
        WriteAssembly(anchor, "FrozenAnchor", "public sealed class Anchor { }");
        BindingConfiguration configuration = Configuration() with { api = Configuration().api with { projectionInputs = new() { references = [facade, anchor] } } };
        await using BindingHost host = Builder().Build();
        GenerationRequest request = new(configuration, scratch.root);
        Success(await host.GenerateAsync(request), "43L", "miss");
        GenerationResult warm = await host.GenerateAsync(request);
        Success(warm, "43L", "hit");
        string receiptPath = Assert.Single(warm.artifacts, artifact => artifact.relativePath.EndsWith("/metadata/verification.json", StringComparison.Ordinal)).relativePath;
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(warm.outputRoot, receiptPath)));
        JsonElement inputs = receipt.RootElement.GetProperty("managedProjectionInputs");
        Assert.Contains(inputs.GetProperty("files").EnumerateArray(), input => input.GetProperty("path").GetString() == dependency);
        string shadow = Path.Combine(first, dependencyName + ".dll");
        Assert.Contains(inputs.GetProperty("absentInputs").EnumerateArray(), input => input.GetProperty("path").GetString() == shadow);
        File.Copy(dependency, shadow);
        Success(await host.GenerateAsync(request), "43L", "miss");
        File.Delete(shadow);
        File.Delete(dependency);
        Dictionary<string, byte[]> before = Published(warm.outputRoot);
        GenerationResult missing = await host.GenerateAsync(request);
        Assert.False(missing.success);
        Assert.Contains(missing.diagnostics, diagnostic => diagnostic.message.Contains(dependencyName, StringComparison.Ordinal));
        SamePublication(warm.outputRoot, before);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("metadata")]
    [InlineData("absent-shadow")]
    public async Task LateManagedInputMutationCannotReplaceACompletePublication(string kind)
    {
        using Scratch scratch = new();
        string source = Path.Combine(scratch.root, "values.cs");
        string referenceName = "LateValues" + Guid.NewGuid().ToString("N");
        string reference = Path.Combine(scratch.root, referenceName + ".dll");
        BindingConfiguration configuration;
        Action mutate;
        if (kind == "source")
        {
            File.WriteAllText(source, Source(43));
            configuration = Configuration() with { api = Configuration().api with { projectionInputs = new() { sources = [source] } } };
            mutate = () => File.WriteAllText(source, Source(44));
        }
        else if (kind == "metadata")
        {
            WriteAssembly(reference, referenceName, Source(43));
            configuration = Configuration() with { api = Configuration().api with { projectionInputs = new() { references = [reference] } } };
            mutate = () => WriteAssembly(reference, referenceName, Source(44));
        }
        else
        {
            string primary = Directory.CreateDirectory(Path.Combine(scratch.root, "primary")).FullName;
            string fallback = Directory.CreateDirectory(Path.Combine(scratch.root, "fallback")).FullName;
            string dependencyName = "LateDependency" + Guid.NewGuid().ToString("N");
            string dependency = Path.Combine(fallback, dependencyName + ".dll");
            WriteAssembly(dependency, dependencyName, "public struct Dependency { public int Value; }");
            reference = Path.Combine(primary, referenceName + ".dll");
            WriteAssembly(reference, referenceName, Source(43) + "public static class UsesDependency { public static Dependency Get() => default; }", dependency);
            string anchor = Path.Combine(fallback, "LateAnchor.dll");
            WriteAssembly(anchor, "LateAnchor", "public struct Anchor { }");
            configuration = Configuration() with { api = Configuration().api with { projectionInputs = new() { references = [reference, anchor] } } };
            mutate = () => File.Copy(dependency, Path.Combine(primary, dependencyName + ".dll"));
        }
        GenerationRequest request = new(configuration, scratch.root);
        await using (BindingHost original = Builder().Build()) Success(await original.GenerateAsync(request), "43L", "miss");
        string output = Path.Combine(scratch.root, "Generated");
        Dictionary<string, byte[]> before = Published(output);
        await using BindingHost changed = Builder().AddArtifactProcessor("fixture.mutate-managed-input", 0, () => new Mutator(mutate)).Build();
        GenerationResult rejected = await changed.GenerateAsync(request);
        Assert.False(rejected.success);
        Assert.Contains(rejected.diagnostics, diagnostic => diagnostic.message.Contains("Managed projection inputs changed", StringComparison.Ordinal));
        SamePublication(output, before);
    }

    [Fact]
    public async Task UnselectedPartialMethodBodiesRemainConsumerOwnedWhileSelectedValuesMustResolve()
    {
        using Scratch scratch = new();
        string source = Path.Combine(scratch.root, "values.cs");
        string partial = "namespace Generated.External { public partial class ExternalApi { public object Describe() => Raw; } }";
        File.WriteAllText(source, Source(43) + partial);
        BindingConfiguration configuration = Configuration() with { api = Configuration().api with { projectionInputs = new() { sources = [source] } } };
        await using BindingHost host = Builder().Build();
        GenerationResult valid = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, scratch.root), compileManaged: true);
        Success(valid, "43L", "miss");
        Dictionary<string, byte[]> before = Published(valid.outputRoot);
        File.WriteAllText(source, Source(44) + partial.Replace("=> Raw", "=> UnrelatedMissingMember", StringComparison.Ordinal));
        GenerationResult generated = await host.GenerateAsync(new(configuration with { output = configuration.output with { root = "Preview" } }, scratch.root));
        Assert.True(generated.success, generated.GetDiagnosticSummary());
        Assert.Equal(Published(valid.outputRoot).Keys.Order(StringComparer.Ordinal), before.Keys.Order(StringComparer.Ordinal));
        File.WriteAllText(source, Source(44).Replace("44L", "SelectedMissingValue", StringComparison.Ordinal));
        GenerationResult selected = await host.GenerateAsync(new(configuration, scratch.root));
        Assert.False(selected.success);
        Assert.Contains(selected.diagnostics, diagnostic => diagnostic.message.Contains("SelectedMissingValue", StringComparison.Ordinal));
        SamePublication(valid.outputRoot, before);
    }

    private static void Success(GenerationResult result, string expression, string cache)
    {
        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.Equal(cache, Assert.Single(result.cacheStages, stage => stage.stage == "csharpEmission").status);
        string generated = string.Join("\n", result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
            .Select(artifact => File.ReadAllText(Path.Combine(result.outputRoot, artifact.relativePath))));
        Assert.Contains("Presented = " + expression, generated, StringComparison.Ordinal);
    }

    private static string Source(int value) => "namespace Vendor { public static class Values { public const long Answer = " + value + "L; } }";
    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 1, library = new() { id = "external_constants", nativeName = "external_constants" },
        source = new() { headers = ["native.h"], includeTransitivelyReferencedHeaders = false },
        api = new() { @namespace = "Generated.External", name = "ExternalApi", constants = [new("SELECTED") { name = "Presented", managedType = "long", expression = "global::Vendor.Values.Answer" }] }
    };

    private static void WriteAssembly(string path, string name, string source, params string[] additional)
    {
        string framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetDirectoryName(path) == framework).Concat(additional).Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source, new(LanguageVersion.CSharp14))], references,
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream stream = new();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        File.WriteAllBytes(path, stream.ToArray());
    }

    private static Dictionary<string, byte[]> Published(string output) => Directory.GetFiles(output, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(output, path), File.ReadAllBytes, StringComparer.Ordinal);
    private static void SamePublication(string output, Dictionary<string, byte[]> before)
    {
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Published(output).Keys.Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(output, path)));
    }

    private sealed class Mutator(Action mutate) : IArtifactProcessor
    {
        public string id => "fixture.mutate-managed-input";
        public int priority => 0;
        public ValueTask ProcessAsync(ArtifactProcessingRequest request, CancellationToken cancellationToken)
        { mutate(); return ValueTask.CompletedTask; }
    }

    private static BindingHostBuilder Builder(string? verifiedWorker = null)
        => BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(verifiedWorker ?? CompiledWorkerRoot());

    private static string CompiledWorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }

    private sealed class VerifiedWorker : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-managed-constant-worker-" + Guid.NewGuid().ToString("N"));
        internal VerifiedWorker()
        {
            string compiled = CompiledWorkerRoot();
            Directory.CreateDirectory(root);
            foreach (string file in Directory.GetFiles(compiled).Where(file => Path.GetFileName(file) != "worker-assets.manifest.json"))
                File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
            string native = Path.Combine("runtimes", RuntimeInformation.RuntimeIdentifier, "native");
            Directory.CreateDirectory(Path.Combine(root, native));
            foreach (string file in Directory.GetFiles(Path.Combine(compiled, native)))
                File.Copy(file, Path.Combine(root, native, Path.GetFileName(file)));
            WorkerAssetManifest manifest = new(1, RuntimeInformation.RuntimeIdentifier, WorkerProtocol.C_PARSER_IDENTITY,
                Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(file =>
                {
                    using FileStream stream = File.OpenRead(file);
                    return new ArtifactDescriptor(Path.GetRelativePath(root, file).Replace('\\', '/'), "application/octet-stream",
                        new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))));
                }).ToImmutableArray());
            File.WriteAllText(Path.Combine(root, "worker-assets.manifest.json"), JsonSerializer.Serialize(manifest));
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }

    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-managed-constant-host-" + Guid.NewGuid().ToString("N"));
        internal Scratch() { Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "native.h"), "#define SELECTED 42\nint echo(int value);\n"); }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
