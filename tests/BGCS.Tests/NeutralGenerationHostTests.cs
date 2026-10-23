using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Artifacts;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core;
using BGCS.Infrastructure.Verification;
using Xunit;
using BGCS.Core.Plugins;
using BGCS.Core.Common;

namespace BGCS.Tests;

public sealed class NeutralGenerationHostTests
{
    [Fact]
    public async Task ExtensionInputInsideFormalOutput_IsRejectedWithoutReplacingPreviousArtifacts()
    {
        using Scratch scratch = new();
        GenerationRequest request = new(Configuration(), scratch.root);
        await using (BindingHost baseline = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend()).Build())
            Assert.True((await baseline.GenerateAsync(request)).success);
        string input = Path.Combine(scratch.root, "Generated", "generation.report.json");
        byte[] previous = File.ReadAllBytes(input);
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IArtifactProcessor>(new("formal-input", ExtensionPort.ArtifactProcessor, 0), _ => new FormalInputProcessor(input)).Build();
        GenerationResult result = await host.GenerateAsync(request);
        Assert.False(result.success);
        Assert.Contains("formal BGCS output", result.GetDiagnosticSummary());
        Assert.Equal(previous, File.ReadAllBytes(input));
    }

    private sealed class FormalInputProcessor(string input) : IArtifactProcessor, IExternalInputProvider
    {
        public string id => "formal-input";
        public int priority => 0;
        public ImmutableArray<DeclaredExternalInput> GetExternalInputs(
            ExternalInputContext context,
            CancellationToken cancellationToken
        ) => [new(input, ExternalInputKind.File)];
        public ValueTask ProcessAsync(
            ArtifactProcessingRequest request,
            CancellationToken cancellationToken
        ) {
            _ = File.ReadAllBytes(input);
            return ValueTask.CompletedTask;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeclaredExtensionInputMutation_BlocksPublicationAndPreservesPreviousOutput(bool initiallyPresent)
    {
        using Scratch scratch = new();
        GenerationRequest request = new(Configuration(), scratch.root);
        await using (BindingHost baseline = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend()).Build())
            Assert.True((await baseline.GenerateAsync(request)).success);
        string report = Path.Combine(scratch.root, "Generated", "generation.report.json");
        byte[] previous = File.ReadAllBytes(report);
        string input = Path.Combine(scratch.root, "extension-state.dat");
        if (initiallyPresent) File.WriteAllText(input, "before");
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IArtifactProcessor>(new("external", ExtensionPort.ArtifactProcessor, 0), _ => new ExternalProcessor(input))
            .RegisterFactory<IGenerationValidator>(new("mutate-input", ExtensionPort.GenerationValidator, 0), _ => new ExternalMutationValidator(input)).Build();
        GenerationResult result = await host.GenerateAsync(request);
        Assert.False(result.success);
        Assert.Contains("external inputs changed", result.GetDiagnosticSummary());
        Assert.Equal(previous, File.ReadAllBytes(report));
    }

    private sealed class ExternalProcessor(string input) : IArtifactProcessor, ICacheFingerprintProvider
    {
        public string id => "external";
        public int priority => 0;
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        )
            => new(new ContentDigest(new string('a', 64)), [new(input, ExternalInputKind.File, allowMissing: true)]);
        public async ValueTask ProcessAsync(
            ArtifactProcessingRequest request,
            CancellationToken cancellationToken
        ) {
            ArtifactDescriptor artifact = request.artifacts.First(candidate => candidate.mediaType == "text/x-csharp");
            string text = await request.content.ReadTextAsync(artifact.relativePath, cancellationToken);
            await request.content.ReplaceTextAsync(artifact.relativePath, text + "\n// external state: " + (File.Exists(input) ? File.ReadAllText(input) : "absent") + "\n", cancellationToken);
        }
    }

    private sealed class ExternalMutationValidator(string input) : IGenerationValidator
    {
        public string id => "mutate-input";
        public int priority => 0;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(
            ValidationRequest request,
            CancellationToken cancellationToken
        ) {
            File.WriteAllText(input, "after");
            return ValueTask.FromResult(ImmutableArray<BindingDiagnostic>.Empty);
        }
    }

    [Fact]
    public async Task CriticalValidatorDiagnostic_BlocksFormalPublication()
    {
        using Scratch scratch = new();
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IGenerationValidator>(new("fatal", ExtensionPort.GenerationValidator, 0), _ => new CriticalValidator()).Build();
        GenerationResult result = await host.GenerateAsync(new(Configuration(), scratch.root));
        Assert.False(result.success);
        Assert.False(result.published);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.severity == BindingDiagnosticSeverity.Critical && diagnostic.isFailure);
        Assert.False(Directory.Exists(result.outputRoot));
    }

    private sealed class CriticalValidator : IGenerationValidator
    {
        public string id => "fatal";
        public int priority => 0;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(
            ValidationRequest request,
            CancellationToken cancellationToken
        )
            => ValueTask.FromResult(ImmutableArray.Create(new BindingDiagnostic(BindingDiagnosticSeverity.Critical, "A mandatory gate failed critically.", "BGCS-TEST-CRITICAL")));
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task ArtifactProcessing_RechecksActualImportsAndPreservesPreviousOutputOnFailure(BindingImportMode mode)
    {
        using Scratch scratch = new();
        BindingConfiguration configuration = Configuration() with { api = Configuration().api with { importMode = mode } };
        ProbeProcessor? successful = null;
        await using (BindingHost initial = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IArtifactProcessor>(new("annotate", ExtensionPort.ArtifactProcessor, 0), _ => successful = new("annotate", 0)).Build())
        {
            GenerationResult result = await initial.GenerateAsync(new(configuration, scratch.root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            new ArtifactDigestVerifier(TestHostPaths.Create()).Verify(result.outputRoot, result.artifacts, [".bgcs-artifacts.json"]);
            Assert.True(successful!.disposed);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => successful.retained!.ReplaceTextAsync(successful.path!, "late", CancellationToken.None).AsTask());
        }
        string output = Path.Combine(scratch.root, "Generated");
        string before = File.ReadAllText(Path.Combine(output, "generation.report.json"));
        ProbeProcessor? corrupt = null;
        await using BindingHost failing = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IArtifactProcessor>(new("corrupt", ExtensionPort.ArtifactProcessor, 0), _ => corrupt = new("corrupt", 0, changeAbi: true)).Build();
        GenerationResult failure = await failing.GenerateAsync(new(configuration, scratch.root));
        Assert.True(corrupt!.changedAbi, "The processor must change an actual native export before exercising the final contract guard.");
        Assert.False(failure.success);
        Assert.Contains("actual", failure.GetDiagnosticSummary());
        Assert.True(corrupt!.disposed);
        Assert.Equal(before, File.ReadAllText(Path.Combine(output, "generation.report.json")));
        Assert.Empty(Directory.GetDirectories(scratch.root, ".bgcs-staging-*"));
    }

    [Fact]
    public async Task ArtifactProcessorHelpers_AreManifestedCompiledAndConfinedToTheManagedTarget()
    {
        using Scratch scratch = new();
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IArtifactProcessor>(new("helper", ExtensionPort.ArtifactProcessor, 0), _ => new ProbeProcessor("helper", 0, addHelper: true)).Build();
        BindingConfiguration configuration = Configuration() with { runtime = new() { mode = "source" } };
        GenerationRequest request = new(configuration, scratch.root);
        GenerationResult result = await host.GenerateAsync(request);
        Assert.True(result.success, result.GetDiagnosticSummary());
        await NativeConsumerHarness.CompileManagedAsync(result, request);
        Assert.Contains(result.artifacts, artifact => artifact.relativePath.EndsWith("/managed/Helpers/ExtensionHelper.g.cs", StringComparison.Ordinal));
        new ArtifactDigestVerifier(TestHostPaths.Create()).Verify(result.outputRoot, result.artifacts, [".bgcs-artifacts.json"]);
        GenerationRequest singleRequest = new(configuration with { output = new() { root = "Single", layout = "singleFile" } }, scratch.root);
        GenerationResult single = await host.GenerateAsync(singleRequest);
        Assert.True(single.success, single.GetDiagnosticSummary());
        await NativeConsumerHarness.CompileManagedAsync(single, singleRequest);
        ArtifactDescriptor composed = Assert.Single(single.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp"));
        Assert.EndsWith("/managed/Bindings.cs", composed.relativePath);
        Assert.Contains("class ExtensionHelper", File.ReadAllText(Path.Combine(single.outputRoot, composed.relativePath)));
    }

    [Fact]
    public async Task ArtifactProcessorReleaseFailure_BlocksPublicationAndClosesItsEditSurface()
    {
        using Scratch scratch = new();
        ProbeProcessor? processor = null;
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IArtifactProcessor>(new("release", ExtensionPort.ArtifactProcessor, 0), _ => processor = new("release", 0, releaseFailure: true)).Build();
        GenerationResult result = await host.GenerateAsync(new(Configuration(), scratch.root));
        Assert.False(result.success);
        Assert.Contains("processor cleanup failed", result.GetDiagnosticSummary());
        Assert.False(Directory.Exists(result.outputRoot));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => processor!.retained!.ReplaceTextAsync(processor.path!, "late", CancellationToken.None).AsTask());
    }

    private sealed class ProbeProcessor(
        string identity,
        int order,
        bool changeAbi = false,
        bool releaseFailure = false,
        bool addHelper = false
    ) : IArtifactProcessor, IDisposable
    {
        public string id => identity;
        public int priority => order;
        public bool disposed { get; private set; }
        public bool changedAbi { get; private set; }
        public IArtifactEditor? retained { get; private set; }
        public string? path { get; private set; }
        public async ValueTask ProcessAsync(
            ArtifactProcessingRequest request,
            CancellationToken cancellationToken
        ) {
            retained = request.content;
            foreach (ArtifactDescriptor artifact in request.artifacts)
            {
                path = artifact.relativePath;
                string original = await request.content.ReadTextAsync(artifact.relativePath, cancellationToken);
                string revised = changeAbi ? original.Replace("\"echo\"", "\"echo_tampered\"", StringComparison.Ordinal) : original + "\n// processed candidate\n";
                changedAbi |= changeAbi && !string.Equals(original, revised, StringComparison.Ordinal);
                await request.content.ReplaceTextAsync(artifact.relativePath, revised, cancellationToken);
            }
            if (addHelper)
                await request.content.AddTextAsync(request.api.interop.targetId + "/managed/Helpers/ExtensionHelper.g.cs", "text/x-csharp",
                    "namespace Generated.Example; public static class ExtensionHelper { public static int Count => 42; }", cancellationToken);
        }
        public void Dispose() { disposed = true; if (releaseFailure) throw new IOException("processor cleanup failed"); }
    }

    [Fact]
    public void GroupedSchema_UsesTheActualStrictSerializerAndDeclaredDefaults()
    {
        var schema = new SchemaCatalog().CreateBindingSchema();
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(2, schema["properties"]!["schemaVersion"]!["const"]!.GetValue<int>());
        Assert.Equal("functionTable", schema["properties"]!["api"]!["properties"]!["importMode"]!["default"]!.GetValue<string>());
        Assert.Equal("instance", schema["properties"]!["api"]!["properties"]!["style"]!["default"]!.GetValue<string>());
        Assert.Equal("package", schema["properties"]!["runtime"]!["properties"]!["mode"]!["default"]!.GetValue<string>());
        Assert.False(schema["properties"]!["semantics"]!["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public async Task NeutralInteropTransforms_RunInFrozenOrderAndReleaseInReverseOrder()
    {
        using Scratch scratch = new();
        System.Collections.Generic.List<string> trace = [];
        BindingHostBuilder builder = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IInteropTransform>(new("second", ExtensionPort.InteropTransform, 0), _ => new ProbeInterop("second", 0, trace))
            .RegisterFactory<IInteropTransform>(new("first", ExtensionPort.InteropTransform, 1), _ => new ProbeInterop("first", 1, trace));
        await using BindingHost host = builder.Build();
        builder.RegisterFactory<IInteropTransform>(new("later", ExtensionPort.InteropTransform, 9), _ => new ProbeInterop("later", 9, trace));
        GenerationResult result = await host.GenerateAsync(new(Configuration(), scratch.root));
        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.Equal(new[] { "first", "second", "dispose:second", "dispose:first" }, trace);
        Assert.Throws<InvalidOperationException>(() => builder.RegisterFactory<INativeModuleTransform>(new("first", ExtensionPort.NativeModuleTransform, 0), _ => null!));
    }

    [Fact]
    public async Task UnverifiedInteropSignatureChange_IsRejectedBeforePublication()
    {
        using Scratch scratch = new();
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IInteropTransform>(new("rewrite", ExtensionPort.InteropTransform, 0), _ => new ProbeInterop("rewrite", 0, [], rewriteAbi: true)).Build();
        GenerationResult result = await host.GenerateAsync(new(Configuration(), scratch.root));
        Assert.False(result.success);
        Assert.Contains("validated bridge", result.GetDiagnosticSummary());
        Assert.False(Directory.Exists(result.outputRoot));
    }

    private sealed class ProbeInterop(
        string identity,
        int ordering,
        System.Collections.Generic.List<string> trace,
        bool rewriteAbi = false
    ) : IInteropTransform, IDisposable
    {
        public string id => identity;
        public int priority => ordering;
        public BGCS.Core.Interop.InteropModule Transform(
            BGCS.Core.Interop.InteropModule module,
            AnalysisContext context,
            CancellationToken cancellationToken
        ) {
            Assert.Equal(module.targetId, context.target.targetId.value);
            trace.Add(identity);
            var functions = module.functions.Select(function => function with { exportName = rewriteAbi ? "changed" : function.exportName, documentation = identity }).ToImmutableArray();
            return new(module.libraryId, module.targetId, module.pointerSize, module.types, functions, module.diagnostics) { byteOrder = module.byteOrder };
        }
        public void Dispose() => trace.Add("dispose:" + identity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitTextConfiguration_BuildsOnlyTheDeclaredNonretainingFriendlyInput(bool retains)
    {
        using Scratch scratch = new();
        BindingConfiguration configuration = Configuration() with
        {
            runtime = new() { mode = "source" },
            semantics = new()
            {
                stringParameters = [new() { function = "echo", parameter = "value",
                encoding = BGCS.Core.Interop.NativeTextEncoding.Utf8, codeUnitWidth = 1, lengthUnit = BGCS.Core.Interop.NativeLengthUnit.CodeUnits,
                direction = BGCS.Core.Interop.NativeParameterDirection.Input, lifetime = BGCS.Core.Interop.NativeLifetimeKind.Call,
                zeroTerminated = true, retainsPointer = retains, allowsNull = false }]
            }
        };
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend(text: true)).Build();
        GenerationRequest request = new(configuration, scratch.root);
        GenerationResult result = await host.GenerateAsync(request);
        Assert.True(result.success, result.GetDiagnosticSummary());
        await NativeConsumerHarness.CompileManagedAsync(result, request);
        string methods = File.ReadAllText(Path.Combine(result.outputRoot, TestComposition.HostTargetId() + "/managed/Api/NativeApi.Methods.g.cs"));
        if (retains)
        {
            Assert.DoesNotContain("Echo(string", methods);
            Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "BGCS-FRIENDLY-OMITTED");
        }
        else Assert.Contains("Echo(string", methods);
        using JsonDocument abi = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.outputRoot, TestComposition.HostTargetId() + "/metadata/abi.manifest.json")));
        int evidence = abi.RootElement.GetProperty("module").GetProperty("functions")[0].GetProperty("parameters")[0].GetProperty("text").GetProperty("evidence").GetProperty("level").GetInt32();
        Assert.Equal((int)BGCS.Core.Common.EvidenceLevel.UserAsserted, evidence);
    }

    [Fact]
    public async Task GenerateAsync_UsesNeutralFactsAndPublishesAccurateStageMetadata()
    {
        using Scratch scratch = new();
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend()).Build();
        GenerationResult result = await host.GenerateAsync(new(Configuration(), scratch.root));
        Assert.True(result.success, result.GetDiagnosticSummary());
        new ArtifactDigestVerifier(TestHostPaths.Create()).Verify(result.outputRoot, result.artifacts, [".bgcs-artifacts.json"]);
        string managed = File.ReadAllText(Path.Combine(result.outputRoot, TestComposition.HostTargetId() + "/managed/Api/NativeApi.g.cs"));
        Assert.Contains("NativeModuleSession", managed);
        Assert.Contains("delegate* unmanaged[Cdecl]<int, int>", managed);
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.outputRoot, TestComposition.HostTargetId() + "/metadata/verification.json")));
        Assert.True(receipt.RootElement.GetProperty("generated").GetBoolean());
        Assert.False(receipt.RootElement.TryGetProperty("managedCompiled", out _));
        Assert.False(receipt.RootElement.TryGetProperty("nativeLinked", out _));
        Assert.False(receipt.RootElement.TryGetProperty("invoked", out _));
        Assert.False(receipt.RootElement.TryGetProperty("packaged", out _));
    }

    [Fact]
    public async Task BackendReleaseFailure_PreventsReplacingPreviousPublishedArtifacts()
    {
        using Scratch scratch = new();
        await using (BindingHost initial = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend()).Build())
            Assert.True((await initial.GenerateAsync(new(Configuration(), scratch.root))).success);
        string output = Path.Combine(scratch.root, "Generated");
        string before = File.ReadAllText(Path.Combine(output, "generation.report.json"));
        await using BindingHost failing = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IBindingBackend>(new("fixture.backend", ExtensionPort.BindingBackend), _ => new ReleaseFailureBackend()).Build();
        GenerationResult result = await failing.GenerateAsync(new(Configuration(), scratch.root));
        Assert.False(result.success);
        Assert.False(result.published);
        Assert.Empty(result.artifacts);
        Assert.Contains("release failed", result.GetDiagnosticSummary());
        Assert.Equal(before, File.ReadAllText(Path.Combine(output, "generation.report.json")));
        Assert.Empty(Directory.GetDirectories(scratch.root, ".bgcs-staging-*"));
    }

    [Fact]
    public async Task SourceRuntime_UsesAnApiLocalNamespaceAndPublishesItsExactClosure()
    {
        using Scratch scratch = new();
        BindingConfiguration configuration = Configuration() with { runtime = new() { mode = "source" }, output = new() { layout = "singleFile" } };
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend()).Build();
        GenerationResult result = await host.GenerateAsync(new(configuration, scratch.root));
        Assert.True(result.success, result.GetDiagnosticSummary());
        ArtifactDescriptor managed = Assert.Single(result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp"));
        string source = File.ReadAllText(Path.Combine(result.outputRoot, managed.relativePath));
        Assert.Contains("namespace Generated.Example.Runtime", source);
        Assert.Contains("global::Generated.Example.Runtime.NativeModuleSession", source);
        Assert.DoesNotContain("class NativeCallbackRegistry", source);
        Assert.DoesNotContain("namespace BGCS.Runtime", source);
    }


    [Fact]
    public void NewConfiguration_StrictlyRejectsUnknownNestedFieldsAndOldFlatInputs()
    {
        using Scratch scratch = new();
        string path = Path.Combine(scratch.root, "bindgen.json");
        File.WriteAllText(path, "{\"schemaVersion\":2,\"library\":{\"id\":\"example\",\"nativeName\":\"example\"},\"source\":{\"headers\":[\"native.h\"],\"typo\":true}}");
        Assert.Throws<JsonException>(() => TestInputs.LoadRequest(path));
        File.WriteAllText(path, "{\"apiName\":\"OldApi\",\"libName\":\"old\"}");
        Assert.Throws<JsonException>(() => TestInputs.LoadRequest(path));
        File.WriteAllText(path, "{\"schemaVersion\":2,\"library\":{\"id\":\"example\",\"nativeName\":\"example\"},\"source\":{\"headers\":[\"native.h\"]}}");
        GenerationRequest request = TestInputs.LoadRequest(path);
        Assert.Equal(scratch.root, request.baseDirectory);
        Assert.Equal("c17", request.configuration.source.languageStandard);
        Assert.Equal(BGCS.Core.BindingImportMode.FunctionTable, request.configuration.api.importMode);
    }

    [Fact]
    public async Task WrongFrontendTarget_DiscardsTheJointCandidate()
    {
        using Scratch scratch = new();
        BindingConfiguration configuration = Configuration() with { targets = [new() { id = "host" }] };
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend(forceTarget: true)).Build();
        GenerationResult result = await host.GenerateAsync(new(configuration, scratch.root));
        Assert.False(result.success);
        Assert.Contains("frontend result changed", result.GetDiagnosticSummary());
        Assert.False(Directory.Exists(result.outputRoot));
    }

    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 2,
        library = new() { id = "example", nativeName = "example" },
        source = new() { headers = ["native.h"] },
        targets = [new() { id = "host" }],
        api = new() { @namespace = "Generated.Example", name = "NativeApi" },
        cache = new() { enabled = false }
    };

    private sealed class ScalarFrontend(
        bool forceTarget = false,
        bool text = false
    ) : IFrontend
    {
        public ValueTask<FrontendPreparation> PrepareAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FrontendPreparation(request, new([], []), []));
        }

        public ValueTask<FrontendResult> ParseAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
            NativeType octet = new(new("char"), NativeTypeCategory.Primitive, "char", 1, 1) { primitiveKind = "Char", isSigned = true };
            NativeType pointer = new(new("text-pointer"), NativeTypeCategory.Pointer, "char*", TestComposition.PointerSize(request.target), TestComposition.PointerSize(request.target)) { elementType = octet.id };
            NativeDeclaration parameter = new(new("parameter"), "Parameter", "value", "echo::value", text ? pointer.id : integer.id, new("native.h", 1, 1))
            { parent = new("function") };
            NativeDeclaration function = new(new("function"), "Function", "echo", "echo", null, new("native.h", 1, 1))
            { returnType = integer.id, children = [parameter.id], callingConvention = "C" };
            NativeModule module = new(forceTarget ? "fixture-wrong-target" : request.target.targetId.value, request.target.triple, TestComposition.PointerSize(request.target), text ? [integer, octet, pointer] : [integer], [parameter, function], [])
            { language = SourceLanguage.C, byteOrder = request.target.byteOrder };
            return ValueTask.FromResult(new FrontendResult(module, new([], [])));
        }
    }

    private sealed class ReleaseFailureBackend : IBindingBackend, IDisposable
    {
        public ValueTask<ImmutableArray<ArtifactDescriptor>> EmitAsync(
            BGCS.Core.Managed.ManagedApiModel model,
            EmissionRequest request,
            CancellationToken cancellationToken
        )
            => new BGCS.CSharp.CSharpBackend().EmitAsync(model, request, cancellationToken);
        public void Dispose() => throw new IOException("backend release failed");
    }

    private sealed class Scratch : IDisposable
    {
        internal readonly string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-host-" + Guid.NewGuid().ToString("N"));
        internal Scratch()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "native.h"), "int echo(int value);");
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
