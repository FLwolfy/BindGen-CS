using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using BGCS.Core.Extensibility;
using BGCS.Cpp2C.Build;
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
            config.Lowerings.Register(new Int32TypeLowering());
            config.Lowerings.Register(new RenameAddLowering());

            Assert.Contains(config.Lowerings.TypeLowerings, lowering => lowering.Name == "builtin.utf8-string");
            Assert.Equal("test.int32", config.Lowerings.TypeLowerings[0].Name);

            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);

            Assert.True(generator.LastResult?.Success);
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
    public void Registry_IsDeterministicAndRejectsDuplicates()
    {
        CppLoweringRegistry registry = new();
        registry.Register(new Int32TypeLowering("z-low", 1));
        registry.Register(new Int32TypeLowering("a-high", 2));

        Assert.Equal(new[] { "a-high", "z-low" }, registry.TypeLowerings.Select(lowering => lowering.Name));
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
                LoweringSafetyPolicy = CppLoweringSafetyPolicy.AllowUserAsserted
            };
            config.TypeLowerings.Add(new()
            {
                Name = "demo.token",
                TypePattern = "Demo::Token",
                CAbiType = "int32_t",
                Marshalling = MarshallingStrategy.Blittable,
                Ownership = BindingOwnership.Borrowed,
                ParameterToCppExpression = "Demo::Token::FromRaw({value})",
                ReturnToCExpression = "({value}).Raw()",
                Safety = CppLoweringSafety.UserAsserted
            });
            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);

            Assert.True(generator.LastResult?.Success,
                string.Join(Environment.NewLine, generator.LastResult?.Diagnostics.Select(value => value.Message) ?? []));
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
            Name = "asserted",
            TypePattern = "int",
            CAbiType = "int32_t",
            Marshalling = MarshallingStrategy.Blittable,
            Safety = CppLoweringSafety.UserAsserted
        };
        config.TypeLowerings.Add(recipe);
        Cpp2CCodeGenerator generator = new(config);
        string temp = CreateTemp("safety");
        string header = Path.Combine(temp, "safety.hpp");
        File.WriteAllText(header, "int identity(int value) { return value; }");
        try
        {
            generator.Generate(header, Path.Combine(temp, "rejected"));
            Assert.False(generator.LastResult?.Success);
            Assert.Contains(generator.LastResult!.Diagnostics, diagnostic => diagnostic.Message.Contains("rejected by policy", StringComparison.Ordinal));

            Cpp2CGeneratorConfig accepted = new() { LoweringSafetyPolicy = CppLoweringSafetyPolicy.AllowUserAsserted };
            accepted.TypeLowerings.Add(recipe);
            Cpp2CCodeGenerator acceptedGenerator = new(accepted);
            acceptedGenerator.Generate(header, Path.Combine(temp, "accepted"));
            Assert.True(acceptedGenerator.LastResult?.Success);

            recipe.Safety = CppLoweringSafety.Unsafe;
            Cpp2CGeneratorConfig unsafeRejected = new() { LoweringSafetyPolicy = CppLoweringSafetyPolicy.AllowUserAsserted };
            unsafeRejected.TypeLowerings.Add(recipe);
            Cpp2CCodeGenerator unsafeGenerator = new(unsafeRejected);
            unsafeGenerator.Generate(header, Path.Combine(temp, "unsafe-rejected"));
            Assert.False(unsafeGenerator.LastResult?.Success);

            Cpp2CGeneratorConfig bypassed = new() { LoweringSafetyPolicy = CppLoweringSafetyPolicy.AllowUnsafe };
            bypassed.TypeLowerings.Add(recipe);
            Cpp2CCodeGenerator bypassedGenerator = new(bypassed);
            bypassedGenerator.Generate(header, Path.Combine(temp, "unsafe-accepted"));
            Assert.True(bypassedGenerator.LastResult?.Success);
            Assert.Contains(bypassedGenerator.LastResult!.Diagnostics,
                diagnostic => diagnostic.Code == BindingDiagnosticCodes.UnsafeLowering &&
                    diagnostic.Severity == BindingDiagnosticSeverity.Warning);

            recipe.TypePattern = "NoMatch";
            string secondHeader = Path.Combine(temp, "safe-second-run.hpp");
            File.WriteAllText(secondHeader, "float identity_float(float value) { return value; }");
            bypassedGenerator.Generate(secondHeader, Path.Combine(temp, "safe-second-run"));
            Assert.True(bypassedGenerator.LastResult?.Success);
            Assert.DoesNotContain(bypassedGenerator.LastResult!.Diagnostics,
                diagnostic => diagnostic.Code == BindingDiagnosticCodes.UnsafeLowering);
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
                LoweringSafetyPolicy = CppLoweringSafetyPolicy.AllowUserAsserted
            };
            config.NativeShims.Add(new()
            {
                Name = "custom",
                PublicHeaders = [shimHeader],
                SourceFiles = [shimSource]
            });
            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);

            Assert.True(generator.LastResult?.Success,
                string.Join(Environment.NewLine, generator.LastResult?.Diagnostics.Select(value => value.Message) ?? []));
            Assert.Contains("extensions/custom/custom_shim.h", File.ReadAllText(Path.Combine(output, "include", "Classes.h")), StringComparison.Ordinal);
            string manifestPath = Path.Combine(output, config.BuildManifestFileName);
            CppBridgeBuildManifest manifest = CppBridgeBuildManifestSerializer.Load(manifestPath);
            Assert.Contains(config.NamePrefix + "BUILD_SHARED", manifest.Defines);
            Assert.Contains(manifest.PublicHeaderFiles, path => path.EndsWith("extensions/custom/custom_shim.h", StringComparison.Ordinal));
            Assert.Contains(manifest.SourceFiles, path => path.EndsWith("extensions/custom/custom_shim.cpp", StringComparison.Ordinal));
            Assert.Contains("bgcs_custom_twice", NativeExportInspector.ReadExpectedSymbols(
                manifest.PublicHeaderFiles.Select(path => Path.GetFullPath(path, output))));
            Assert.True(CompileGeneratedBridge(output, temp, library, out string diagnostics), diagnostics);
            Assert.True(NativeExportInspector.Inspect(manifest, manifestPath, library).Success);
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
            config.Lowerings.Register(new ProjectionContributor());
            config.TypeLowerings.Add(new()
            {
                Name = "custom.value",
                TypePattern = "Custom::Value",
                CAbiType = "int32_t",
                ManagedProjection = new("CustomValue", "{value}.Value", "new CustomValue({value})")
            });

            CppExtensionArtifactEmitter.EmitNative(config, native);
            string managed = Path.Combine(temp, "managed");
            CppExtensionArtifactEmitter.EmitManaged(config, managed);

            Assert.True(File.Exists(Path.Combine(native, "include", "extensions", "projection.h")));
            Assert.True(File.Exists(Path.Combine(native, "src", "extensions", "projection.cpp")));
            Assert.Contains("extensions/projection.h", File.ReadAllText(Path.Combine(native, "include", "Classes.h")), StringComparison.Ordinal);
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
            Assert.True(first.LastResult?.Success);
            Assert.False(first.LastResult!.CacheHit);
            Assert.Contains("configured_plugin_add", File.ReadAllText(Path.Combine(temp, "GeneratedBridge", "include", "Classes.h")), StringComparison.Ordinal);

            Cpp2CCodeGenerator second = new(Cpp2CGeneratorConfig.Load(configPath));
            second.GenerateConfigured();
            Assert.True(second.LastResult?.Success);
            Assert.True(second.LastResult!.CacheHit);
            Assert.Equal(first.LastResult.CacheKey, second.LastResult.CacheKey);
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
        public string Name { get; } = name;
        public int Priority { get; } = priority;
        public bool CanLower(CppType type, CppTypeLoweringContext context) => type is CppPrimitiveType { Kind: CppPrimitiveKind.Int };
        public CppTypeLoweringPlan CreatePlan(CppType type, CppTypeLoweringContext context) =>
            new(Name, CppTypeLoweringKind.Custom, "int32_t", MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
    }

    private sealed class RenameAddLowering : ICppCallableLowering
    {
        public string Name => "test.rename-add";
        public int Priority => 100;
        public bool CanLower(CppFunction function, CppCallableLoweringContext context) => function.Name == "add";
        public CppCallableLoweringPlan CreatePlan(CppFunction function, CppCallableLoweringContext context) =>
            new(Name, "custom_add") { InvocationExpression = "checked_add({invocation})" };
    }

    private sealed class ProjectionContributor : ICppArtifactContributor, ICacheFingerprintProvider
    {
        public string Name => "test.projection";
        public int Priority => 100;
        public string GetCacheFingerprint() => "projection-v1";
        public IReadOnlyList<CppGeneratedArtifact> Contribute(CppArtifactContext context) => context.Stage switch
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
    public string Id => "bgcs.tests.cpp-lowering-plugin";
    public string Version => "1.0.0";
    public int ContractVersion => BindingPluginContract.CurrentVersion;
    public string GetCacheFingerprint() => "configured-cpp-lowering-final";

    public void Configure(IBindingPluginHost host) =>
        host.Register<ICppCallableLowering>("configured-plugin-callable", new CallableLowering());

    private sealed class CallableLowering : ICppCallableLowering, ICacheFingerprintProvider
    {
        public string Name => "configured-plugin-callable";
        public int Priority => 100;
        public bool CanLower(CppFunction function, CppCallableLoweringContext context) => function.Name == "plugin_add";
        public CppCallableLoweringPlan CreatePlan(CppFunction function, CppCallableLoweringContext context) =>
            new(Name, "configured_plugin_add");
        public string GetCacheFingerprint() => "final";
    }
}
