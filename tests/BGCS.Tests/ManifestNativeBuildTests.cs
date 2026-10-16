using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Ports;
using BGCS.Contracts;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Requests;
using BGCS.NativeBuild.Packaging;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.Tests;

public sealed class ManifestNativeBuildTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Echo(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BridgeEcho(int value, out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BridgeValue(out int result, out nint error);

    [Fact]
    public async Task MsvcFactVerificationUsesOriginalInputWithoutCompilingAnotherTranslationUnit()
    {
        if (!OperatingSystem.IsWindows()) return;
        string temporary = NewTemporary();
        nint module = 0;
        try
        {
            BindingConfiguration configuration = await PrepareAsync(temporary, cpp: true);
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.hpp"), "class Widget { public: Widget() = default; int Value() const { return 42; } };\ninline int widget_value() { Widget value; return value.Value(); }\n");
            configuration = configuration with { nativeBuild = configuration.nativeBuild with { provider = "msvc" } };
            await using BindingHost host = CreateHost();
            GenerationResult generated = await host.GenerateAsync(new(configuration, temporary));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            string manifestPath = FindManifest(generated.outputRoot);
            NativeBuildManifest manifest = JsonSerializer.Deserialize<NativeBuildManifest>(await File.ReadAllTextAsync(manifestPath), WorkerProtocol.jsonOptions)!;
            NativeManifestCompilationUnit factSource = Assert.Single(manifest.preprocessingSources);
            Assert.DoesNotContain(manifest.sources, source => source.path == factSource.path);
            string factsPath = Path.Combine(generated.outputRoot, factSource.path["artifact:/".Length..]);
            string facts = await File.ReadAllTextAsync(factsPath);
            Assert.Contains("native.hpp", facts);
            Assert.DoesNotContain("#include <type_traits>", facts);
            ManifestNativeBuildResult built = await host.NativeBuildAsync(new(manifestPath));
            Assert.True(built.success, built.GetDiagnosticSummary());
            Assert.Equal(Path.GetFullPath(factsPath), Path.GetFullPath(Assert.Single(built.plan!.preprocessingOperations, operation => operation.preprocessingOnly).source));
            CppBridgeModule bridge = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(manifestPath)!, "bridge.module.json")))!;
            string symbol = Assert.Single(bridge.exports, export => export.operation.kind == BridgeOperationKind.FreeCall).name;
            module = NativeLibrary.Load(built.nativeArtifact!);
            Assert.Equal(0, Marshal.GetDelegateForFunctionPointer<BridgeValue>(NativeLibrary.GetExport(module, symbol))(out int result, out nint error));
            Assert.Equal(42, result);
            Assert.Equal(0, error);
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedNeutralManifestBuildsInvokesAndRetainsGeneratedArtifacts(bool cpp)
    {
        string temporary = NewTemporary();
        nint module = 0;
        try
        {
            BindingConfiguration configuration = await PrepareAsync(temporary, cpp);
            await using BindingHost host = CreateHost();
            GenerationResult generated = await host.GenerateAsync(new(configuration, temporary));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            string manifest = FindManifest(generated.outputRoot);
            using (JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(manifest)))
            {
                Assert.Equal("manifest_native", json.RootElement.GetProperty("importModuleName").GetString());
                Assert.Equal("DynamicLibrary", json.RootElement.GetProperty("managedRuntime").GetProperty("moduleMechanism").GetString());
                Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("nativeArtifactFilename").GetString()));
            }
            Dictionary<string, byte[]> previous = ReadArtifacts(generated.outputRoot);
            ManifestNativeBuildPlanResult preview = await host.PlanNativeBuildAsync(new(manifest));
            Assert.True(preview.success, preview.GetDiagnosticSummary());
            Assert.NotNull(preview.nativeArtifact);
            Assert.False(File.Exists(preview.nativeArtifact));
            Assert.Equal(previous.Keys.Order(), ReadArtifacts(generated.outputRoot).Keys.Order());
            string packageRoot = Path.Combine(temporary, "Native package 验收");
            ManifestNativeBuildResult built = await host.NativeBuildAsync(new(manifest) { packageRoot = packageRoot });
            Assert.True(built.success, built.GetDiagnosticSummary());
            Assert.True(built.exportEvidence!.success);
            Assert.NotEmpty(built.packageArtifacts);
            Assert.True(File.Exists(Path.Combine(packageRoot, "bgcs.native-assets.json")));
            foreach ((string relative, byte[] content) in previous)
                if (relative != ".bgcs-artifacts.json") Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(generated.outputRoot, relative)));
            module = NativeLibrary.Load(built.nativeArtifact!);
            if (cpp)
            {
                string modelFile = Path.Combine(Path.GetDirectoryName(manifest)!, "bridge.module.json");
                CppBridgeModule model = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(modelFile))!;
                BridgeExport callable = Assert.Single(model.exports, export => export.operation.kind == BridgeOperationKind.FreeCall);
                BridgeEcho call = Marshal.GetDelegateForFunctionPointer<BridgeEcho>(NativeLibrary.GetExport(module, callable.name));
                Assert.Equal(0, call(21, out int result, out nint error));
                Assert.Equal(42, result);
                Assert.Equal(0, error);
            }
            else Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<Echo>(NativeLibrary.GetExport(module, "twice"))(21));
            NativeLibrary.Free(module);
            module = 0;
            Dictionary<string, byte[]> published = ReadArtifacts(generated.outputRoot);
            string changed = Path.Combine(temporary, cpp ? "native.hpp" : "native.c");
            await File.AppendAllTextAsync(changed, "\n// changed locked input\n");
            ManifestNativeBuildResult failed = await host.NativeBuildAsync(new(manifest) { packageRoot = packageRoot });
            Assert.False(failed.success);
            Assert.Contains("changed after", failed.GetDiagnosticSummary());
            AssertArtifactsEqual(published, generated.outputRoot);
            Assert.Empty(Directory.GetFileSystemEntries(temporary, ".bgcs-set-*"));
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public async Task BrowserManifestPreservesRegisteredImportIdentityBeforeProviderActivation()
    {
        string temporary = NewTemporary();
        try
        {
            string source = Path.Combine(temporary, "native.c");
            await File.WriteAllTextAsync(source, "int twice(int value) { return value * 2; }\n");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("emscripten-wasm32-emscripten")));
            ManagedRuntimeProfile profile = ManagedRuntimeResolver.Resolve(target, ManagedRuntimeKind.BrowserWasm);
            byte[] sourceBytes = await File.ReadAllBytesAsync(source);
            NativeBuildManifest manifest = new("browser-manifest", "api", target, [new(source, SourceLanguage.C, "c17")], [],
                [new(source, source, sourceBytes.Length, new(Convert.ToHexStringLower(SHA256.HashData(sourceBytes))))], ["twice"])
            {
                outputKind = NativeOutputKind.StaticLibrary, providerId = "fixture-browser-planner", managedRuntime = profile,
                importModuleName = "api", nativeArtifactFilename = "api.a"
            };
            string manifestPath = Path.Combine(temporary, "bridge.manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, WorkerProtocol.jsonOptions));
            int activations = 0;
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns()
                .AddNativeBuildProvider("fixture-browser-planner", () => { activations++; return new BrowserPreviewPlanner(); }).Build();
            ManifestNativeBuildPlanResult defaultPlan = await host.PlanNativeBuildAsync(new(manifestPath));
            Assert.True(defaultPlan.success, defaultPlan.GetDiagnosticSummary());
            Assert.Equal("api.a", Path.GetFileName(defaultPlan.nativeArtifact));
            Assert.Equal(1, activations);
            ManifestNativeBuildPlanResult invalid = await host.PlanNativeBuildAsync(new(manifestPath) { outputFile = Path.Combine(temporary, "libapi.a") });
            Assert.False(invalid.success);
            Assert.Contains("changes the generated managed import identity", invalid.GetDiagnosticSummary());
            Assert.Equal(1, activations);
            ManifestNativeBuildPlanResult valid = await host.PlanNativeBuildAsync(new(manifestPath) { outputFile = Path.Combine(temporary, "different directory", "api.a") });
            Assert.True(valid.success, valid.GetDiagnosticSummary());
            Assert.Equal(2, activations);
            Assert.False(Directory.Exists(Path.Combine(temporary, "different directory")));
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest with { nativeArtifactFilename = "libapi.a" }, WorkerProtocol.jsonOptions));
            ManifestNativeBuildPlanResult altered = await host.PlanNativeBuildAsync(new(manifestPath));
            Assert.False(altered.success);
            Assert.Contains("changes its generated managed import identity", altered.GetDiagnosticSummary());
            Assert.Equal(2, activations);
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest with { managedRuntime = null }, WorkerProtocol.jsonOptions));
            ManifestNativeBuildPlanResult incomplete = await host.PlanNativeBuildAsync(new(manifestPath));
            Assert.False(incomplete.success);
            Assert.Contains("together", incomplete.GetDiagnosticSummary());
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private sealed class BrowserPreviewPlanner : INativeBuildProvider
    {
        public string id => "fixture-browser-planner";
        public NativeBuildPlan Plan(NativeBuildRequest request, CancellationToken cancellationToken)
            => new(id, request.target, [], request.outputFile) { outputKind = request.outputKind, scratchDirectory = request.workingDirectory };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeFlagsChangingSameSizedRecordFieldTypesCannotReplaceVerifiedOutput(bool cpp)
    {
        string temporary = NewTemporary();
        try
        {
            BindingConfiguration configuration = await PrepareAsync(temporary, cpp);
            string header = Path.Combine(temporary, cpp ? "native.hpp" : "native.h");
            await File.WriteAllTextAsync(header,
                "#ifdef BGCS_ALTER_FIELD\ntypedef float NativeScalar;\n#else\ntypedef int NativeScalar;\n#endif\n"
                + "struct NativeRecord { NativeScalar value; };\n"
                + (cpp ? "inline int twice(NativeRecord* record) { return (int)record->value * 2; }\n" : "int twice(struct NativeRecord* record);\n"));
            if (!cpp) await File.WriteAllTextAsync(Path.Combine(temporary, "native.c"), NativeExport + "#include \"native.h\"\nEXPORT int twice(struct NativeRecord* record) { return (int)record->value * 2; }\n");
            await using BindingHost host = CreateHost();
            BuildResult built = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            string assertions = Assert.Single(Directory.GetFiles(built.outputRoot, "storage.assertions.*", SearchOption.AllDirectories));
            Assert.Contains("native field type differs from parsed facts", await File.ReadAllTextAsync(assertions));
            Dictionary<string, byte[]> previous = ReadArtifacts(built.outputRoot);
            configuration = configuration with { nativeBuild = configuration.nativeBuild with { compilerArguments = ["-DBGCS_ALTER_FIELD"] } };
            BuildResult failed = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.False(failed.success);
            Assert.Contains("Native effective source facts differ from the generated contract", failed.GetDiagnosticSummary());
            AssertArtifactsEqual(previous, built.outputRoot);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task FinalNativeConstructorParameterTypeCannotChangeThroughAnImplicitConversion()
    {
        string temporary = NewTemporary();
        try
        {
            BindingConfiguration configuration = await PrepareAsync(temporary, cpp: true);
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.hpp"), """
                #ifdef BGCS_ALTER_FACT
                using NativeInput = float;
                #else
                using NativeInput = int;
                #endif
                class NativeRecord {
                    int value;
                public:
                    explicit NativeRecord(NativeInput input) : value((int)input) {}
                    int Read() const { return value; }
                };
                inline int twice(int value) { return value * 2; }
                """);
            await using BindingHost host = CreateHost();
            BuildResult built = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            Dictionary<string, byte[]> previous = ReadArtifacts(built.outputRoot);
            BindingConfiguration divergent = configuration with { nativeBuild = configuration.nativeBuild with { compilerArguments = ["-DBGCS_ALTER_FACT"] } };
            BuildResult failed = await host.BuildAsync(new(new(divergent, temporary)));
            Assert.False(failed.success);
            Assert.Contains("constructor signature", failed.GetDiagnosticSummary());
            AssertArtifactsEqual(previous, built.outputRoot);
            GenerationResult generated = await host.GenerateAsync(new(divergent, temporary));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            Dictionary<string, byte[]> generatedArtifacts = ReadArtifacts(generated.outputRoot);
            ManifestNativeBuildResult standalone = await host.NativeBuildAsync(new(FindManifest(generated.outputRoot)));
            Assert.False(standalone.success);
            Assert.Contains("constructor signature", standalone.GetDiagnosticSummary());
            AssertArtifactsEqual(generatedArtifacts, generated.outputRoot);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalNativeNonAddressableStorageCannotDriftWhileSizeAndPublicSignaturesStayEqual(bool privateField)
    {
        string temporary = NewTemporary();
        try
        {
            BindingConfiguration configuration = await PrepareAsync(temporary, cpp: privateField);
            string header = Path.Combine(temporary, privateField ? "native.hpp" : "native.h");
            string declarations = privateField ? """
                class NativeRecord {
                #ifdef BGCS_ALTER_FACT
                    float value;
                #else
                    int value;
                #endif
                public:
                    explicit NativeRecord(int input) : value(input) {}
                    int Read() const { return (int)value; }
                };
                inline int twice(int value) { return value * 2; }
                """ : """
                struct NativeRecord {
                #ifdef BGCS_ALTER_FACT
                    unsigned int low : 2;
                #else
                    unsigned int low : 1;
                #endif
                    unsigned int high : 2;
                };
                int twice(int value);
                """;
            await File.WriteAllTextAsync(header, declarations);
            await using BindingHost host = CreateHost();
            BuildResult built = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            Dictionary<string, byte[]> previous = ReadArtifacts(built.outputRoot);
            configuration = configuration with { nativeBuild = configuration.nativeBuild with { compilerArguments = ["-DBGCS_ALTER_FACT"] } };
            BuildResult failed = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.False(failed.success);
            Assert.Contains("Native effective source facts differ", failed.GetDiagnosticSummary());
            AssertArtifactsEqual(previous, built.outputRoot);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task CompilerOnlyHeaderMutationDuringPackagerReleasePreservesNativeAndPackageSets()
    {
        string temporary = NewTemporary();
        try
        {
            BindingConfiguration configuration = await PrepareAsync(temporary, cpp: false);
            string header = Path.Combine(temporary, "build-only.h");
            await File.WriteAllTextAsync(header, "#define NATIVE_FACTOR 2\n");
            string source = Path.Combine(temporary, "native.c");
            await File.WriteAllTextAsync(source, NativeExport + "\n#include \"build-only.h\"\nEXPORT int twice(int value) { return value * NATIVE_FACTOR; }\n");
            string packageRoot = Path.Combine(temporary, "native-assets");
            string manifest;
            await using (BindingHost host = CreateHost())
            {
                GenerationResult generated = await host.GenerateAsync(new(configuration, temporary));
                Assert.True(generated.success, generated.GetDiagnosticSummary());
                manifest = FindManifest(generated.outputRoot);
                using (JsonDocument serialized = JsonDocument.Parse(await File.ReadAllTextAsync(manifest)))
                    Assert.Contains(serialized.RootElement.GetProperty("extensionInputs").EnumerateArray()
                        .SelectMany(snapshot => snapshot.GetProperty("excludedDirectories").EnumerateArray()), exclusion => exclusion.GetString() == "artifact:/");
                ManifestNativeBuildResult first = await host.NativeBuildAsync(new(manifest) { packageRoot = packageRoot });
                Assert.True(first.success, first.GetDiagnosticSummary());
                string originalManifest = await File.ReadAllTextAsync(manifest);
                JsonObject changedManifest = JsonNode.Parse(originalManifest)!.AsObject();
                JsonArray ownedExclusions = changedManifest["extensionInputs"]!.AsArray().Select(snapshot => snapshot!["excludedDirectories"]!.AsArray())
                    .First(exclusions => exclusions.Any(exclusion => exclusion!.GetValue<string>() == "artifact:/"));
                int markerIndex = Enumerable.Range(0, ownedExclusions.Count).First(index => ownedExclusions[index]!.GetValue<string>() == "artifact:/");
                ownedExclusions[markerIndex] = "artifact:/../outside";
                await File.WriteAllTextAsync(manifest, changedManifest.ToJsonString());
                ManifestNativeBuildPlanResult escaping = await host.PlanNativeBuildAsync(new(manifest));
                Assert.False(escaping.success);
                Assert.Contains("within the declared native artifact root", escaping.GetDiagnosticSummary());
                await File.WriteAllTextAsync(manifest, originalManifest);
            }
            string outputRoot = Path.Combine(temporary, "Generated");
            Dictionary<string, byte[]> previous = ReadArtifacts(outputRoot), package = ReadArtifacts(packageRoot);
            MutatingPackager? service = null;
            await using BindingHost mutating = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot())
                .AddNativeArtifactPackager("mutating-packager", () => service = new(() => File.WriteAllText(header, "#define NATIVE_FACTOR 3\n"))).Build();
            ManifestNativeBuildResult failed = await mutating.NativeBuildAsync(new(manifest) { packageRoot = packageRoot });
            Assert.False(failed.success);
            Assert.True(service!.disposed);
            Assert.Contains("dependencies changed before publication", failed.GetDiagnosticSummary());
            AssertArtifactsEqual(previous, outputRoot);
            AssertArtifactsEqual(package, packageRoot);
            Assert.Empty(Directory.GetFileSystemEntries(temporary, ".bgcs-set-*"));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
    [Fact]
    public async Task FactoryOnlyPluginRequiresMatchingHostAndItsNativeValidationGateCannotBeBypassed()
    {
        string temporary = NewTemporary();
        try
        {
            BindingConfiguration configuration = await PrepareAsync(temporary, cpp: false);
            NativeGuardState state = new();
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot())
                .AddPluginFactory(NativeGuardPlugin.description, () => new NativeGuardPlugin(state), pathBase: temporary).Build();
            GenerationResult generated = await host.GenerateAsync(new(configuration, temporary));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            string manifest = FindManifest(generated.outputRoot);
            using (JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(manifest)))
            {
                Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("plugins")[0].GetProperty("manifestPath").ValueKind);
                Assert.Equal("manifest-guard", json.RootElement.GetProperty("plugins")[0].GetProperty("descriptor").GetProperty("id").GetString());
            }
            await using (BindingHost missing = CreateHost())
            {
                ManifestNativeBuildPlanResult rejected = await missing.PlanNativeBuildAsync(new(manifest));
                Assert.False(rejected.success);
                Assert.Contains("matching host SDK plugin registration", rejected.GetDiagnosticSummary());
            }
            ManifestNativeBuildResult built = await host.NativeBuildAsync(new(manifest));
            Assert.True(built.success, built.GetDiagnosticSummary());
            Assert.Equal(1, state.nativeValidations);
            Dictionary<string, byte[]> previous = ReadArtifacts(generated.outputRoot);
            state.rejectNative = true;
            ManifestNativeBuildResult blocked = await host.NativeBuildAsync(new(manifest));
            Assert.False(blocked.success);
            Assert.Contains("native gate rejected", blocked.GetDiagnosticSummary());
            Assert.Equal(2, state.nativeValidations);
            AssertArtifactsEqual(previous, generated.outputRoot);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NativeFlagsChangingSameSizedCallableTypesCannotReplaceVerifiedOutput(bool cpp, bool alias)
    {
        string temporary = NewTemporary();
        try
        {
            BindingConfiguration configuration = await PrepareAsync(temporary, cpp);
            string header = Path.Combine(temporary, cpp ? "native.hpp" : "native.h");
            await File.WriteAllTextAsync(header, alias
                ? "#ifdef BGCS_ALTER_SIGNATURE\ntypedef float NativeScalar;\n#else\ntypedef int NativeScalar;\n#endif\n" + (cpp ? "inline NativeScalar twice(NativeScalar value) { return value * 2; }\n" : "NativeScalar twice(NativeScalar value);\n")
                : cpp
                ? "#ifdef BGCS_ALTER_SIGNATURE\ninline float twice(float value) { return value * 2; }\n#else\ninline int twice(int value) { return value * 2; }\n#endif\n"
                : "#ifdef BGCS_ALTER_SIGNATURE\nfloat twice(float value);\n#else\nint twice(int value);\n#endif\n");
            if (!cpp) await File.WriteAllTextAsync(Path.Combine(temporary, "native.c"), NativeExport + "#include \"native.h\"\n" + (alias
                ? "EXPORT NativeScalar twice(NativeScalar value) { return value * 2; }\n"
                : "#ifdef BGCS_ALTER_SIGNATURE\nEXPORT float twice(float value) { return value * 2; }\n#else\nEXPORT int twice(int value) { return value * 2; }\n#endif\n"));
            await using BindingHost host = CreateHost();
            BuildResult first = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.True(first.success, first.GetDiagnosticSummary());
            Dictionary<string, byte[]> previous = ReadArtifacts(first.outputRoot);
            BindingConfiguration divergent = configuration with
            {
                nativeBuild = configuration.nativeBuild with { compilerArguments = ["-DBGCS_ALTER_SIGNATURE=1"] }
            };
            TargetInspection inspection = Assert.Single(first.generation.targetModels);
            NativeType originalReturn = NativeFactDriftAssertions.Return(inspection.native);
            Assert.Equal("Int", originalReturn.primitiveKind);
            Assert.Equal(4, originalReturn.size);
            NativeModule effective = await NativeFactDriftAssertions.ObserveAsync(host, divergent, temporary, inspection.resolvedTarget!, divergent.nativeBuild.compilerArguments);
            NativeType changedReturn = NativeFactDriftAssertions.Return(effective);
            Assert.Equal("Float", changedReturn.primitiveKind);
            Assert.Equal(originalReturn.size, changedReturn.size);
            NativeDeclaration effectiveFunction = Assert.Single(effective.declarations, declaration => declaration.kind == "Function" && declaration.name == "twice");
            NativeDeclaration effectiveParameter = Assert.Single(effectiveFunction.children.Select(effective.GetDeclaration), declaration => declaration.kind == "Parameter");
            Assert.Equal("Float", NativeFactDriftAssertions.Canonical(effective, effectiveParameter.type!.Value).primitiveKind);
            BuildResult rejected = await host.BuildAsync(new(new(divergent, temporary)));
            NativeFactDriftAssertions.Reject(rejected, "twice (callable signature)");
            AssertArtifactsEqual(previous, first.outputRoot);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task NativeEnumUnderlyingSignednessDriftCannotReplaceVerifiedOutputWhenTheEnumNameAndSizeMatch()
    {
        string temporary = NewTemporary();
        try
        {
            BindingConfiguration configuration = await PrepareAsync(temporary, cpp: true);
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.hpp"), """
                #ifdef BGCS_ALTER_ENUM_SIGN
                enum class NativeState : unsigned int { Value = 21 };
                #else
                enum class NativeState : int { Value = 21 };
                #endif
                inline NativeState twice(NativeState value) { return value; }
                """);
            await using BindingHost host = CreateHost();
            BuildResult first = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.True(first.success, first.GetDiagnosticSummary());
            Dictionary<string, byte[]> previous = ReadArtifacts(first.outputRoot);
            BindingConfiguration divergent = configuration with { nativeBuild = configuration.nativeBuild with { compilerArguments = ["-DBGCS_ALTER_ENUM_SIGN=1"] } };
            TargetInspection inspection = Assert.Single(first.generation.targetModels);
            NativeType originalEnum = NativeFactDriftAssertions.Return(inspection.native);
            Assert.Equal(NativeTypeCategory.Enumeration, originalEnum.category);
            NativeType originalInteger = NativeFactDriftAssertions.Canonical(inspection.native, originalEnum.elementType!.Value);
            Assert.Equal(true, originalInteger.isSigned);
            NativeModule effective = await NativeFactDriftAssertions.ObserveAsync(host, divergent, temporary, inspection.resolvedTarget!, divergent.nativeBuild.compilerArguments);
            NativeType changedEnum = NativeFactDriftAssertions.Return(effective);
            NativeType changedInteger = NativeFactDriftAssertions.Canonical(effective, changedEnum.elementType!.Value);
            Assert.Equal(false, changedInteger.isSigned);
            Assert.Equal(originalEnum.size, changedEnum.size);
            Assert.Equal(originalInteger.size, changedInteger.size);
            BuildResult rejected = await host.BuildAsync(new(new(divergent, temporary)));
            NativeFactDriftAssertions.Reject(rejected, "NativeState.element (exact native type or storage facts)");
            AssertArtifactsEqual(previous, first.outputRoot);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task NativeCharSignednessDriftCannotReplaceVerifiedOutputEvenWhenPrototypeSpellingAndSizeMatch()
    {
        string temporary = NewTemporary();
        try
        {
            BindingConfiguration configuration = await PrepareAsync(temporary, cpp: false);
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.h"), "char twice(char value);\n");
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.c"), NativeExport + "EXPORT char twice(char value) { return value; }\n");
            configuration = configuration with { source = configuration.source with { arguments = ["-fsigned-char"] } };
            await using BindingHost host = CreateHost();
            BuildResult first = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.True(first.success, first.GetDiagnosticSummary());
            Dictionary<string, byte[]> previous = ReadArtifacts(first.outputRoot);
            BindingConfiguration divergent = configuration with { nativeBuild = configuration.nativeBuild with { compilerArguments = ["-funsigned-char"] } };
            TargetInspection inspection = Assert.Single(first.generation.targetModels);
            NativeType originalChar = NativeFactDriftAssertions.Return(inspection.native);
            Assert.Equal("Char", originalChar.primitiveKind);
            Assert.Equal(true, originalChar.isSigned);
            NativeModule effective = await NativeFactDriftAssertions.ObserveAsync(host, divergent, temporary, inspection.resolvedTarget!, divergent.nativeBuild.compilerArguments);
            NativeType changedChar = NativeFactDriftAssertions.Return(effective);
            Assert.Equal("Char", changedChar.primitiveKind);
            Assert.Equal(false, changedChar.isSigned);
            Assert.Equal(originalChar.size, changedChar.size);
            BuildResult rejected = await host.BuildAsync(new(new(divergent, temporary)));
            NativeFactDriftAssertions.Reject(rejected, "twice (callable signature)");
            AssertArtifactsEqual(previous, first.outputRoot);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private sealed class NativeGuardState { public int nativeValidations; public bool rejectNative; }
    private sealed class NativeGuardPlugin(NativeGuardState state) : IBindingPlugin
    {
        internal static PluginDescriptor description { get; } = new("manifest-guard", "1.0.0", [new("manifest-guard/validator", ExtensionPort.GenerationValidator)]);
        public PluginDescriptor descriptor => description;
        public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration)
            => registry.RegisterFactory<IGenerationValidator>("manifest-guard/validator", () => new NativeGuard(state));
    }
    private sealed class NativeGuard(NativeGuardState state) : IGenerationValidator
    {
        public string id => "manifest-guard/validator";
        public int priority => 0;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.nativeLinked) return ValueTask.FromResult(ImmutableArray<BindingDiagnostic>.Empty);
            Assert.False(request.managedCompiled);
            Assert.True(request.nativeArtifactVerified);
            Assert.NotEmpty(request.interop.functions);
            state.nativeValidations++;
            return ValueTask.FromResult<ImmutableArray<BindingDiagnostic>>(state.rejectNative ? [new(BindingDiagnosticSeverity.Critical, "native gate rejected", "BGCS-NATIVE-GUARD")] : []);
        }
    }

    private sealed class MutatingPackager(Action mutation) : INativeArtifactPackager, IDisposable
    {
        public string id => "mutating-packager";
        public bool disposed { get; private set; }
        public ValueTask<NativeAssetPackageResult> PackageAsync(NativeArtifactPackagingRequest request, CancellationToken cancellationToken)
            => new NativeAssetPackager().PackageAsync(request, cancellationToken);
        public void Dispose() { disposed = true; mutation(); }
    }
    [Fact]
    public async Task UnifiedBuildPreparesSelectedNativeAssetsAndRejectsPackagerInputMutationBeforePublication()
    {
        string temporary = NewTemporary();
        try
        {
            BindingConfiguration configuration = await PrepareAsync(temporary, cpp: false);
            configuration = configuration with { nativeBuild = configuration.nativeBuild with { packaging = new() { enabled = true, packager = "tracked-packager" } } };
            string policy = Path.Combine(temporary, "package-policy.txt");
            await File.WriteAllTextAsync(policy, "original");
            bool mutate = false;
            TrackedPackager? last = null;
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot())
                .AddNativeArtifactPackager("tracked-packager", () => last = new(policy, () => { if (mutate) File.AppendAllText(policy, "changed"); })).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            Assert.True(last!.disposed);
            Assert.Contains(built.artifacts, artifact => artifact.relativePath.EndsWith("/native/package/bgcs.native-assets.json", StringComparison.Ordinal));
            string receipt = Assert.Single(Directory.GetFiles(built.outputRoot, "verification.json", SearchOption.AllDirectories));
            using (JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(receipt))) Assert.False(json.RootElement.GetProperty("packaged").GetBoolean());
            Dictionary<string, byte[]> previous = ReadArtifacts(built.outputRoot);
            mutate = true;
            BuildResult failed = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.False(failed.success);
            Assert.True(last!.disposed);
            Assert.Contains("external inputs changed", failed.GetDiagnosticSummary());
            AssertArtifactsEqual(previous, built.outputRoot);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
    private sealed class TrackedPackager(string policy, Action release) : INativeArtifactPackager, IExternalInputProvider, IDisposable
    {
        public string id => "tracked-packager";
        public bool disposed { get; private set; }
        public ImmutableArray<DeclaredExternalInput> GetExternalInputs(ExternalInputContext context, CancellationToken cancellationToken)
            => [new(policy, ExternalInputKind.File)];
        public ValueTask<NativeAssetPackageResult> PackageAsync(NativeArtifactPackagingRequest request, CancellationToken cancellationToken)
            => new NativeAssetPackager().PackageAsync(request, cancellationToken);
        public void Dispose() { disposed = true; release(); }
    }
    private const string NativeExport = "#if defined(_WIN32)\n#define EXPORT __declspec(dllexport)\n#else\n#define EXPORT __attribute__((visibility(\"default\")))\n#endif\n";
    private static async Task<BindingConfiguration> PrepareAsync(string temporary, bool cpp)
    {
        await File.WriteAllTextAsync(Path.Combine(temporary, cpp ? "native.hpp" : "native.h"), cpp ? "inline int twice(int value) { return value * 2; }" : "int twice(int value);");
        if (!cpp) await File.WriteAllTextAsync(Path.Combine(temporary, "native.c"), NativeExport + "EXPORT int twice(int value) { return value * 2; }\n");
        return new()
        {
            schemaVersion = 1, library = new() { id = "manifest", nativeName = "manifest_native" },
            source = new() { language = cpp ? SourceLanguage.Cpp : SourceLanguage.C, headers = [cpp ? "native.hpp" : "native.h"], parseMacros = false },
            cache = new() { enabled = false }, runtime = new() { mode = "source" },
            nativeBuild = new() { mode = "sources", sources = cpp ? [] : ["native.c"] }
        };
    }
    private static BindingHost CreateHost() => BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
    private static string NewTemporary() { string path = Path.Combine(Path.GetTempPath(), "bgcs native manifest 验收 & " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static string FindManifest(string root) => Assert.Single(Directory.GetFiles(root, "bridge.manifest.json", SearchOption.AllDirectories));
    private static Dictionary<string, byte[]> ReadArtifacts(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal);
    private static void AssertArtifactsEqual(Dictionary<string, byte[]> previous, string root)
    {
        Dictionary<string, byte[]> actual = ReadArtifacts(root);
        Assert.Equal(previous.Keys.Order(), actual.Keys.Order());
        foreach ((string path, byte[] content) in previous) Assert.Equal(content, actual[path]);
    }
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
