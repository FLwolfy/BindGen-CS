using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Core.Extensibility;
using BGCS.Cpp2C.Build;
using BGCS.Cpp2C.Configuration;
using BGCS.Cpp2C.Facade;
using BGCS.Cpp2C.Lowering;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using BGCS.Intermediate;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class CppLoweringExtensionTests
{
    [Fact]
    public void Registration_PublishesReadOnlySnapshotsWithoutChangingPreviousReaders()
    {
        CppLoweringRegistry registry = new();
        IReadOnlyList<ICppTypeLowering> empty = registry.typeLowerings;
        registry.Register(new Int32TypeLowering());
        IReadOnlyList<ICppTypeLowering> populated = registry.typeLowerings;

        Assert.Empty(empty);
        Assert.Single(populated);
        Assert.Same(populated, registry.typeLowerings);
        Assert.Throws<NotSupportedException>(() => ((IList<ICppTypeLowering>)populated).Clear());
        Assert.Throws<InvalidOperationException>(() => registry.Register(new Int32TypeLowering()));
        Assert.Same(populated, registry.typeLowerings);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UnaryInt(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AddToken(int token, int delta);

    [Fact]
    public void RegisteredLowerings_ChangeTypeCallableAndInvocationWithoutGeneratorChanges()
    {
        string temp = CreateTemp("programmatic");
        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "GeneratedBridge");
        File.WriteAllText(header, "int add(int left, int right) { return left + right; }\n");
        try
        {
            Cpp2CGeneratorConfig config = new();
            config.lowerings.Register(new Int32TypeLowering());
            config.lowerings.Register(new RenameAddLowering());

            Assert.Contains(config.lowerings.typeLowerings, lowering => lowering.name == "builtin.utf8-string");
            Assert.Equal("test.int32", config.lowerings.typeLowerings[0].name);

            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);

            Assert.True(generator.lastResult?.success);
            string generated = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            Assert.Contains("API(int32_t) custom_add(int32_t left, int32_t right);", generated, StringComparison.Ordinal);
            Assert.Contains("checked_add(add(left, right))", File.ReadAllText(Path.Combine(output, "src", "Classes.cpp")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void CacheFingerprintProvidersCanQueryTheRegistryFromAnotherThread()
    {
        string temp = CreateTemp("fingerprint-ownership");
        string header = Path.Combine(temp, "sample.hpp");
        File.WriteAllText(header, "inline int sample(int value) { return value; }\n");
        Cpp2CGeneratorConfig config = new() {
            entryFiles = [header], outputPath = Path.Combine(temp, "Bridge"),
            enableIncrementalCache = true, cacheDirectory = Path.Combine(temp, "Cache"),
            generateBuildManifest = false
        };
        QueryingFingerprintContributor contributor = new(config.lowerings);
        config.lowerings.Register(contributor);
        try
        {
            Cpp2CCodeGenerator generator = new(config);
            generator.GenerateConfigured();
            Assert.True(generator.lastResult?.success,
                string.Join(Environment.NewLine, generator.lastResult?.diagnostics.Select(value => value.message) ?? []));
            Assert.True(contributor.queried);
        }
        finally
        {
            contributor.Drain();
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Registry_IsDeterministicAndRejectsDuplicates()
    {
        CppLoweringRegistry registry = new();
        registry.Register(new Int32TypeLowering("z-low", 1));
        registry.Register(new Int32TypeLowering("a-high", 2));

        Assert.Equal(new[] { "a-high", "z-low" }, registry.typeLowerings.Select(lowering => lowering.name));
        Assert.Throws<InvalidOperationException>(() => registry.Register(new Int32TypeLowering("a-high", 9)));
    }

    [Fact]
    public void DeclarativeRecipe_PerformsRealBidirectionalCustomTypeLowering()
    {
        string temp = CreateTemp("recipe");
        string header = Path.Combine(temp, "token.hpp");
        string output = Path.Combine(temp, "GeneratedBridge");
        string library = Path.Combine(temp, NativeLibraryName("token"));
        File.WriteAllText(header,
            "namespace Demo { struct Token { int value; static Token FromRaw(int value) { return Token{value}; } int Raw() const { return value; } }; " +
            "inline Token Add(Token token, int delta) { return Token{token.value + delta}; } }");
        try
        {
            Cpp2CGeneratorConfig config = new()
            {
                loweringSafetyPolicy = CppLoweringSafetyPolicy.AllowUserAsserted
            };
            config.typeLowerings.Add(new()
            {
                name = "demo.token",
                typePattern = "Demo::Token",
                cAbiType = "int32_t",
                marshalling = MarshallingStrategy.Blittable,
                ownership = BindingOwnership.Borrowed,
                parameterToCppExpression = "Demo::Token::FromRaw({value})",
                returnToCExpression = "({value}).Raw()",
                safety = CppLoweringSafety.UserAsserted
            });
            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);

            Assert.True(generator.lastResult?.success,
                string.Join(Environment.NewLine, generator.lastResult?.diagnostics.Select(value => value.message) ?? []));
            string bridgeHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string bridgeSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("API(int32_t) Demo_Add(int32_t token, int delta)", bridgeHeader, StringComparison.Ordinal);
            Assert.Contains("Demo::Token::FromRaw(token)", bridgeSource, StringComparison.Ordinal);
            Assert.Contains(".Raw()", bridgeSource, StringComparison.Ordinal);
            Assert.True(CompileGeneratedBridge(output, temp, library, out string diagnostics), diagnostics);
            nint native = NativeLibrary.Load(library);
            try
            {
                AddToken add = Marshal.GetDelegateForFunctionPointer<AddToken>(NativeLibrary.GetExport(native, "Demo_Add"));
                Assert.Equal(17, add(12, 5));
            }
            finally
            {
                NativeLibrary.Free(native);
            }
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void SafetyPolicy_RequiresExplicitOptInForAssertedAndUnsafeLowerings()
    {
        Cpp2CGeneratorConfig config = new();
        CppTypeLoweringRecipe recipe = new()
        {
            name = "asserted",
            typePattern = "int",
            cAbiType = "int32_t",
            marshalling = MarshallingStrategy.Blittable,
            safety = CppLoweringSafety.UserAsserted
        };
        config.typeLowerings.Add(recipe);
        Cpp2CCodeGenerator generator = new(config);
        string temp = CreateTemp("safety");
        string header = Path.Combine(temp, "safety.hpp");
        File.WriteAllText(header, "int identity(int value) { return value; }");
        try
        {
            generator.Generate(header, Path.Combine(temp, "rejected"));
            Assert.False(generator.lastResult?.success);
            Assert.Contains(generator.lastResult!.diagnostics, diagnostic => diagnostic.message.Contains("rejected by policy", StringComparison.Ordinal));

            Cpp2CGeneratorConfig accepted = new() { loweringSafetyPolicy = CppLoweringSafetyPolicy.AllowUserAsserted };
            accepted.typeLowerings.Add(recipe);
            Cpp2CCodeGenerator acceptedGenerator = new(accepted);
            acceptedGenerator.Generate(header, Path.Combine(temp, "accepted"));
            Assert.True(acceptedGenerator.lastResult?.success);

            recipe.safety = CppLoweringSafety.Unsafe;
            Cpp2CGeneratorConfig unsafeRejected = new() { loweringSafetyPolicy = CppLoweringSafetyPolicy.AllowUserAsserted };
            unsafeRejected.typeLowerings.Add(recipe);
            Cpp2CCodeGenerator unsafeGenerator = new(unsafeRejected);
            unsafeGenerator.Generate(header, Path.Combine(temp, "unsafe-rejected"));
            Assert.False(unsafeGenerator.lastResult?.success);

            Cpp2CGeneratorConfig bypassed = new() { loweringSafetyPolicy = CppLoweringSafetyPolicy.AllowUnsafe };
            bypassed.typeLowerings.Add(recipe);
            Cpp2CCodeGenerator bypassedGenerator = new(bypassed);
            bypassedGenerator.Generate(header, Path.Combine(temp, "unsafe-accepted"));
            Assert.True(bypassedGenerator.lastResult?.success);
            Assert.Contains(bypassedGenerator.lastResult!.diagnostics,
                diagnostic => diagnostic.code == BindingDiagnosticCodes.C_UNSAFELOWERING &&
                    diagnostic.severity == BindingDiagnosticSeverity.Warning);

            recipe.typePattern = "NoMatch";
            string secondHeader = Path.Combine(temp, "safe-second-run.hpp");
            File.WriteAllText(secondHeader, "float identity_float(float value) { return value; }");
            bypassedGenerator.Generate(secondHeader, Path.Combine(temp, "safe-second-run"));
            Assert.True(bypassedGenerator.lastResult?.success);
            Assert.DoesNotContain(bypassedGenerator.lastResult!.diagnostics,
                diagnostic => diagnostic.code == BindingDiagnosticCodes.C_UNSAFELOWERING);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void ExplicitNativeShim_IsBuiltExportedAndInvoked()
    {
        string temp = CreateTemp("shim");
        string header = Path.Combine(temp, "base.hpp");
        string shimHeader = Path.Combine(temp, "custom_shim.h");
        string shimSource = Path.Combine(temp, "custom_shim.cpp");
        string output = Path.Combine(temp, "GeneratedBridge");
        string library = Path.Combine(temp, NativeLibraryName("shim"));
        File.WriteAllText(header, "namespace Base { inline int Value() { return 3; } }");
        File.WriteAllText(shimHeader, "#pragma once\n#include \"common.h\"\nAPI(int) bgcs_custom_twice(int value);\n");
        File.WriteAllText(shimSource, "#include \"custom_shim.h\"\nAPI_INTERNAL(int) bgcs_custom_twice(int value) { return value * 2; }\n");
        try
        {
            Cpp2CGeneratorConfig config = new()
            {
                loweringSafetyPolicy = CppLoweringSafetyPolicy.AllowUserAsserted
            };
            config.nativeShims.Add(new()
            {
                name = "custom",
                publicHeaders = [shimHeader],
                sourceFiles = [shimSource]
            });
            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);

            Assert.True(generator.lastResult?.success,
                string.Join(Environment.NewLine, generator.lastResult?.diagnostics.Select(value => value.message) ?? []));
            Assert.Contains("extensions/custom/custom_shim.h", File.ReadAllText(Path.Combine(output, "include", "Classes.h")), StringComparison.Ordinal);
            string manifestPath = Path.Combine(output, config.buildManifestFileName);
            CppBridgeBuildManifest manifest = CppBridgeBuildManifestSerializer.Load(manifestPath);
            Assert.Contains(config.namePrefix + "BUILD_SHARED", manifest.defines);
            Assert.Contains(manifest.publicHeaderFiles, path => path.EndsWith("extensions/custom/custom_shim.h", StringComparison.Ordinal));
            Assert.Contains(manifest.sourceFiles, path => path.EndsWith("extensions/custom/custom_shim.cpp", StringComparison.Ordinal));
            Assert.Contains("bgcs_custom_twice", NativeExportInspector.ReadExpectedSymbols(
                manifest.publicHeaderFiles.Select(path => Path.GetFullPath(path, output))));
            Assert.True(CompileGeneratedBridge(output, temp, library, out string diagnostics), diagnostics);
            Assert.True(NativeExportInspector.Inspect(manifest, manifestPath, library).success);
            nint native = NativeLibrary.Load(library);
            try
            {
                UnaryInt twice = Marshal.GetDelegateForFunctionPointer<UnaryInt>(NativeLibrary.GetExport(native, "bgcs_custom_twice"));
                Assert.Equal(42, twice(21));
            }
            finally
            {
                NativeLibrary.Free(native);
            }
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void ArtifactContributor_EmitsNativeAndManagedProjectionArtifacts()
    {
        string temp = CreateTemp("artifacts");
        try
        {
            string native = Path.Combine(temp, "native");
            Directory.CreateDirectory(Path.Combine(native, "include"));
            File.WriteAllText(Path.Combine(native, "include", "Classes.h"), "#pragma once\n");
            Cpp2CGeneratorConfig config = new();
            config.lowerings.Register(new ProjectionContributor());
            config.typeLowerings.Add(new()
            {
                name = "custom.value",
                typePattern = "Custom::Value",
                cAbiType = "int32_t",
                managedProjection = new("CustomValue", "{value}.Value", "new CustomValue({value})")
            });

            new BGCS.Cpp2C.Emission.CBridgeEmitter().Emit(
                new BGCS.Intermediate.Bridges.CppBridgeModule("fixture", [], [], CppExtensionArtifactEmitter.CollectNative(config), []),
                new BGCS.Intermediate.Emission.EmissionContext(native, false, string.Empty));
            string managed = Path.Combine(temp, "managed");
            BGCS.Intermediate.Bridges.CppManagedArtifactPlan managedPlan = CppExtensionArtifactEmitter.CollectManaged(config);
            BindingModule bindings = new("Bridge", config.cSharpNamespace, "bridge", managedPlan.targetId)
            {
                types = [new("BGCS_Projection_custom_value", "Carrier", BindingTypeKind.Alias, 4, 4)
                {
                    underlyingType = new("int", "int", 0, false, 4)
                }]
            };
            CppExtensionArtifactEmitter.EmitManaged(managedPlan, bindings, new(managed, false, string.Empty));

            Assert.True(File.Exists(Path.Combine(native, "include", "extensions", "projection.h")));
            Assert.True(File.Exists(Path.Combine(native, "src", "extensions", "projection.cpp")));
            Assert.Contains(CppExtensionArtifactEmitter.CollectNative(config),
                artifact => artifact.exposeToBindings && artifact.relativePath == "include/extensions/projection.h");
            Assert.Contains("CustomValue", File.ReadAllText(Path.Combine(managed, "Extensions", "CustomValue.cs")), StringComparison.Ordinal);
            string configured = File.ReadAllText(Path.Combine(managed, "Extensions", "ConfiguredLoweringProjections.g.cs"));
            Assert.Contains("ToNative_custom_value", configured, StringComparison.Ordinal);
            Assert.Contains("FromNative_custom_value", configured, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void ConfiguredPlugin_RegistersFinalLoweringContractAndUsesStableCacheFingerprint()
    {
        string temp = CreateTemp("plugin");
        File.WriteAllText(Path.Combine(temp, "sample.hpp"), "int plugin_add(int left, int right) { return left + right; }\n");
        string configPath = Path.Combine(temp, "bridge.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new
        {
            EntryFiles = new[] { "sample.hpp" },
            OutputPath = "GeneratedBridge",
            PluginAssemblies = new[] { typeof(ConfiguredCppLoweringPlugin).Assembly.Location }
        }));
        try
        {
            Cpp2CCodeGenerator first = new(Cpp2CGeneratorConfig.Load(configPath));
            first.GenerateConfigured();
            Assert.True(first.lastResult?.success);
            Assert.False(first.lastResult!.cacheHit);
            Assert.Contains("configured_plugin_add", File.ReadAllText(Path.Combine(temp, "GeneratedBridge", "include", "Classes.h")), StringComparison.Ordinal);

            Cpp2CCodeGenerator second = new(Cpp2CGeneratorConfig.Load(configPath));
            second.GenerateConfigured();
            Assert.True(second.lastResult?.success);
            Assert.True(second.lastResult!.cacheHit);
            Assert.Equal(first.lastResult.cacheKey, second.lastResult.cacheKey);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static string CreateTemp(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), "bgcs-lowering-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string NativeLibraryName(string name) =>
        OperatingSystem.IsWindows() ? name + ".dll" : OperatingSystem.IsMacOS() ? "lib" + name + ".dylib" : "lib" + name + ".so";

    private static bool CompileGeneratedBridge(string output, string sourceDirectory, string library, out string diagnostics)
    {
        string? compiler = CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp);
        if (compiler == null)
        {
            diagnostics = "C++ compiler not available.";
            return false;
        }
        ProcessStartInfo start = new(compiler)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        string[] sources = Directory.GetFiles(Path.Combine(output, "src"), "*.cpp", SearchOption.AllDirectories);
        List<string> arguments = OperatingSystem.IsWindows()
            ? ["-std=c++23", "-shared"]
            : ["-std=c++23", OperatingSystem.IsMacOS() ? "-dynamiclib" : "-shared", "-fPIC"];
        arguments.AddRange(["-I", Path.Combine(output, "include"), "-iquote", sourceDirectory]);
        foreach (string includeDirectory in Directory.GetDirectories(Path.Combine(output, "include"), "*", SearchOption.AllDirectories))
            arguments.AddRange(["-I", includeDirectory]);
        arguments.AddRange(sources);
        arguments.AddRange(["-o", library]);
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        diagnostics = stdout + Environment.NewLine + stderr;
        return process.ExitCode == 0;
    }

    private sealed class Int32TypeLowering(string name = "test.int32", int priority = 100) : ICppTypeLowering
    {
        public string name { get; } = name;
        public int priority { get; } = priority;
        public bool CanLower(CppType type, CppTypeLoweringContext context) => type is CppPrimitiveType { kind: CppPrimitiveKind.Int };
        public CppTypeLoweringPlan CreatePlan(CppType type, CppTypeLoweringContext context) =>
            new(name, CppTypeLoweringKind.Custom, "int32_t", MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
    }

    private sealed class RenameAddLowering : ICppCallableLowering
    {
        public string name => "test.rename-add";
        public int priority => 100;
        public bool CanLower(CppFunction function, CppCallableLoweringContext context) => function.name == "add";
        public CppCallableLoweringPlan CreatePlan(CppFunction function, CppCallableLoweringContext context) =>
            new(name, "custom_add") { invocationExpression = "checked_add({invocation})" };
    }

    private sealed class QueryingFingerprintContributor(CppLoweringRegistry registry) : ICppArtifactContributor, ICacheFingerprintProvider
    {
        private readonly List<Task<int>> m_queries = [];
        public string name => "test.querying-fingerprint";
        public int priority => 100;
        public bool queried { get; private set; }

        public string GetCacheFingerprint()
        {
            Task<int> query = Task.Run(() => registry.typeLowerings.Count);
            m_queries.Add(query);
            if (!query.Wait(TimeSpan.FromSeconds(2)))
                throw new InvalidOperationException("An external fingerprint callback cannot query its registry.");
            queried = true;
            return "querying-fingerprint:" + query.Result;
        }

        public IReadOnlyList<CppGeneratedArtifact> Contribute(CppArtifactContext context) => [];

        public void Drain() => Task.WhenAll(m_queries).Wait(TimeSpan.FromSeconds(5));
    }

    private sealed class ProjectionContributor : ICppArtifactContributor, ICacheFingerprintProvider
    {
        public string name => "test.projection";
        public int priority => 100;
        public string GetCacheFingerprint() => "projection-v1";
        public IReadOnlyList<CppGeneratedArtifact> Contribute(CppArtifactContext context) => context.stage switch
        {
            CppGeneratedArtifactKind.PublicHeader => [new("projection.h", "#pragma once\n", CppGeneratedArtifactKind.PublicHeader, true)],
            CppGeneratedArtifactKind.NativeSource => [new("projection.cpp", "int bgcs_projection_anchor = 0;\n", CppGeneratedArtifactKind.NativeSource)],
            CppGeneratedArtifactKind.ManagedSource => [new("CustomValue.cs", "public readonly record struct CustomValue(int Value);\n", CppGeneratedArtifactKind.ManagedSource)],
            _ => []
        };
    }
}

public sealed class ConfiguredCppLoweringPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    public string id => "bgcs.tests.cpp-lowering-plugin";
    public string version => "1.0.0";
    public int contractVersion => BindingPluginContract.C_CURRENT_VERSION;
    public string GetCacheFingerprint() => "configured-cpp-lowering-final";

    public void Configure(IBindingPluginHost host) =>
        host.Register<ICppCallableLowering>("configured-plugin-callable", new CallableLowering());

    private sealed class CallableLowering : ICppCallableLowering, ICacheFingerprintProvider
    {
        public string name => "configured-plugin-callable";
        public int priority => 100;
        public bool CanLower(CppFunction function, CppCallableLoweringContext context) => function.name == "plugin_add";
        public CppCallableLoweringPlan CreatePlan(CppFunction function, CppCallableLoweringContext context) =>
            new(name, "configured_plugin_add");
        public string GetCacheFingerprint() => "final";
    }
}
