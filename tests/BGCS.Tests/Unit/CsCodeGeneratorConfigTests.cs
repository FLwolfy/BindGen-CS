using System;
using System.IO;
using System.Text.Json;
using BGCS.Configuration;
using BGCS.Configuration.Mapping;
using BGCS.Core.Extensibility;
using BGCS.CppAst.Parsing;
using BGCS.Facade;
using Xunit;

namespace BGCS.Tests;

public class CsCodeGeneratorConfigTests
{
    [Fact]
    public void Constructor_CollectionsShouldBeInitialized()
    {
        CsCodeGeneratorConfig cfg = new();

        Assert.NotNull(cfg.entryFiles);
        Assert.NotNull(cfg.allowedHeaders);
        Assert.NotNull(cfg.includeFolders);
        Assert.NotNull(cfg.systemIncludeFolders);
        Assert.NotNull(cfg.defines);
        Assert.NotNull(cfg.additionalArguments);
        Assert.NotNull(cfg.typeMappings);
        Assert.NotNull(cfg.externalTypeContracts);
        Assert.NotNull(cfg.nameMappings);
        Assert.NotNull(cfg.keywords);
        Assert.NotNull(cfg.functionMappings);
        Assert.NotNull(cfg.arrayMappings);
        Assert.NotNull(cfg.pluginAssemblies);
    }

    [Fact]
    public void Validator_ShouldRejectIncompleteOrConflictingExternalTypeContracts()
    {
        CsCodeGeneratorConfig config = new()
        {
            @namespace = "Test.Generated",
            apiName = "TestApi",
            libName = "test"
        };
        config.typeMappings["NativeValue"] = "ManagedValue";
        config.externalTypeContracts.Add(new()
        {
            nativeTypes = ["NativeValue"],
            managedTypes = ["ManagedValue"],
            size = 0,
            alignment = 3,
            byValuePolicy = (ExternalTypeByValuePolicy)99
        });
        config.externalTypeContracts.Add(new()
        {
            nativeTypes = ["NativeValue"],
            managedTypes = ["ManagedValue"],
            size = 8,
            alignment = 4
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => ConfigValidator.Validate(config));

        Assert.Contains("positive Size", exception.Message, StringComparison.Ordinal);
        Assert.Contains("power of two", exception.Message, StringComparison.Ordinal);
        Assert.Contains("invalid ByValuePolicy", exception.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate native selector", exception.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate managed selector", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NativeVector_*", "NativeVector_int", true)]
    [InlineData("NativeVector_?", "NativeVector_i", true)]
    [InlineData("NativeVector<*>", "NativeVector<int>", true)]
    [InlineData("NativeVector<*>", "OtherVector<int>", false)]
    [InlineData("Exact", "Exact", true)]
    public void ExternalTypeSelector_ShouldUseOrdinalGlobMatching(string selector, string value, bool expected)
    {
        Assert.Equal(expected, ExternalTypeContract.MatchesSelector(selector, value));
    }

    [Fact]
    public void Validator_ShouldRejectInvalidCacheAndPluginSettings()
    {
        CsCodeGeneratorConfig config = new()
        {
            @namespace = "Test.Generated",
            apiName = "TestApi",
            libName = "test",
            cacheDirectory = " ",
            pluginAssemblies = [""]
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => ConfigValidator.Validate(config));

        Assert.Contains("CacheDirectory", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PluginAssemblies", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PresetResolver_ShouldComposeNamedDefaultsAndRejectUnknownNames()
    {
        CsCodeGeneratorConfig config = new() { preset = "windows-c,c-library,opaque-callbacks,function-table" };

        BGCS.Configuration.PresetResolver.@default.Apply(config);

        Assert.Equal(CppParserKind.C, config.parserKind);
        Assert.Equal(ImportType.FunctionTable, config.importType);
        Assert.True(config.useCustomContext);
        Assert.True(config.wrapPointersAsHandle);
        Assert.True(config.delegatesAsVoidPointer);
        Assert.False(config.parseMacros);
        CsCodeGeneratorConfig invalid = new() { preset = "missing-preset" };
        Assert.Throws<InvalidOperationException>(() => BGCS.Configuration.PresetResolver.@default.Apply(invalid));
    }

    [Fact]
    public void ConfigLoader_ExplicitValuesShouldOverridePresetDefaults()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-preset-override-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string path = Path.Combine(temp, "bindings.json");
        File.WriteAllText(path,
            """
            {
              "preset": "c-library",
              "namespace": "Test.Generated",
              "apiName": "TestApi",
              "libName": "test",
              "autoSquashTypedef": true,
              "parseMacros": true,
              "generateExtensions": true,
              "importType": "LibraryImport"
            }
            """);

        try
        {
            CsCodeGeneratorConfig config = new ConfigLoader().Load(path);

            Assert.True(config.autoSquashTypedef);
            Assert.True(config.parseMacros);
            Assert.True(config.generateExtensions);
            Assert.Equal(ImportType.LibraryImport, config.importType);
            Assert.Equal(CppParserKind.C, config.parserKind);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void ConfigLoader_LoadsConfiguredPluginAssemblyExactlyOnce()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-config-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string path = Path.Combine(temp, "bindings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Namespace = "Test.Generated",
            ApiName = "TestApi",
            LibName = "test",
            PluginAssemblies = new[] { typeof(TestConfigPlugin).Assembly.Location }
        }));

        try
        {
            CsCodeGeneratorConfig config = new ConfigLoader().Load(path);

            BindingPluginService<ICacheFingerprintProvider> service = Assert.Single(config.plugins.GetServices<ICacheFingerprintProvider>());
            Assert.Equal("configured-plugin", service.id);
            Assert.Equal("ready", service.service.GetCacheFingerprint());
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void GenerateConfigured_FingerprintedPluginParticipatesInIncrementalCache()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-plugin-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "api.h"), "int plugin_cache_value(void);\n");
        string path = Path.Combine(temp, "bindings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Namespace = "Test.Generated",
            ApiName = "TestApi",
            LibName = "test",
            EntryFiles = new[] { "api.h" },
            OutputPath = "generated",
            MergeGeneratedFilesToSingleFile = true,
            GenerateExtensions = false,
            PluginAssemblies = new[] { typeof(TestConfigPlugin).Assembly.Location }
        }));

        try
        {
            CsCodeGenerator first = CsCodeGenerator.Create(path);
            Assert.True(first.GenerateConfigured());
            Assert.False(first.lastResult!.cacheHit);

            CsCodeGenerator second = CsCodeGenerator.Create(path);
            Assert.True(second.GenerateConfigured());
            Assert.True(second.lastResult!.cacheHit);
            Assert.Equal(first.lastResult.cacheKey, second.lastResult.cacheKey);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void ConfigLoader_ChildExplicitValuesShouldOverrideInheritedPresetDefaults()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-preset-base-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string basePath = Path.Combine(temp, "base.json");
        string childPath = Path.Combine(temp, "child.json");
        File.WriteAllText(basePath, "{\"preset\":\"c-library\",\"namespace\":\"Base.Namespace\",\"apiName\":\"TestApi\",\"libName\":\"test\"}");
        File.WriteAllText(childPath,
            """
            {
              "baseConfig": {
                "url": "file://base.json"
              },
              "autoSquashTypedef": true,
              "parseMacros": true
            }
            """);

        try
        {
            CsCodeGeneratorConfig config = new ConfigLoader().Load(childPath);

            Assert.Equal("Base.Namespace", config.@namespace);
            Assert.Equal("c-library", config.preset);
            Assert.True(config.autoSquashTypedef);
            Assert.True(config.parseMacros);
            Assert.False(config.generateExtensions);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void PrepareSettings_ShouldApplyConfiguredWindowsArchitecture()
    {
        CsCodeGeneratorConfig config = new()
        {
            targetId = "windows-x86-msvc"
        };
        TestGenerator generator = new(config);

        CppParserOptions options = generator.GetParserOptions();

        Assert.Equal("i686-pc-windows-msvc", options.targetTriple);
        Assert.Contains("_WIN32=1", options.defines);
        Assert.Contains("_M_IX86=600", options.defines);
        Assert.DoesNotContain("_WIN64=1", options.defines);
    }

    [Fact]
    public void Merge_WhenFlagsProvided_ShouldMergeOnlySelectedCollections()
    {
        CsCodeGeneratorConfig cfg = new();
        CsCodeGeneratorConfig baseCfg = new();

        cfg.includeFolders.Add("a");
        baseCfg.includeFolders.Add("b");
        baseCfg.defines.Add("D1");

        cfg.Merge(baseCfg, MergeOptions.IncludeFolders);

        Assert.Contains("a", cfg.includeFolders);
        Assert.Contains("b", cfg.includeFolders);
        Assert.Empty(cfg.defines);
    }

    [Fact]
    public void Merge_WhenNoFlags_ShouldNotModifyCollections()
    {
        CsCodeGeneratorConfig cfg = new();
        CsCodeGeneratorConfig baseCfg = new();
        baseCfg.includeFolders.Add("from-base");

        cfg.Merge(baseCfg, MergeOptions.None);

        Assert.Empty(cfg.includeFolders);
    }

    [Fact]
    public void Load_WhenFileMissing_FailsWithoutCreatingConfiguration()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string configPath = Path.Combine(temp, "config.json");

        try
        {
            Assert.False(File.Exists(configPath));
            Assert.Throws<FileNotFoundException>(() => new BGCS.Configuration.ConfigLoader().Load(configPath));
            Assert.False(File.Exists(configPath));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    [Fact]
    public void Load_WhenFileExists_ShouldNotRewriteSourceDocument()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string configPath = Path.Combine(temp, "config.json");
        const string source = "{\"apiName\":\"Test\",\"namespace\":\"Test.Generated\",\"extensionSetting\":42}";
        File.WriteAllText(configPath, source);

        try
        {
            CsCodeGeneratorConfig cfg = new BGCS.Configuration.ConfigLoader().Load(configPath);

            Assert.Equal("Test", cfg.apiName);
            Assert.Equal(source, File.ReadAllText(configPath));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    [Fact]
    public void GenerateConfigured_ShouldResolveInputAndOutputRelativeToConfig()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-configured-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string configPath = Path.Combine(temp, "config.json");
        File.WriteAllText(Path.Combine(temp, "dependency.h"), "#pragma once\ntypedef struct BgcsDependency { int value; } BgcsDependency;");
        File.WriteAllText(Path.Combine(temp, "api.h"), "#include \"dependency.h\"\nint bgcs_ping(BgcsDependency* value);");
        File.WriteAllText(configPath,
            """
            {
              "namespace": "Configured.Generated",
              "apiName": "ConfiguredApi",
              "libName": "configured",
              "entryFiles": [
                "api.h"
              ],
              "includeTransitivelyReferencedHeaders": true,
              "outputPath": "generated",
              "importType": "DllImport",
              "generateExtensions": false,
              "mergeGeneratedFilesToSingleFile": true
            }
            """);

        try
        {
            CsCodeGenerator generator = CsCodeGenerator.Create(configPath);

            bool success = generator.GenerateConfigured();

            Assert.True(success);
            string bindingsPath = Path.Combine(temp, "generated", "Bindings.cs");
            Assert.True(File.Exists(bindingsPath));
            string bindings = File.ReadAllText(bindingsPath);
            Assert.Contains("BgcsPingNative", bindings);
            Assert.Contains("struct BgcsDependency", bindings);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    [Fact]
    public void GenerateConfigured_IncrementalCacheRestoresOutputsAndInvalidatesOnHeaderChange()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-configured-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "api.h");
        string configPath = Path.Combine(temp, "config.json");
        File.WriteAllText(header, "int cache_first(void);\n");
        File.WriteAllText(configPath,
            "{\"Namespace\":\"Cache.Generated\",\"ApiName\":\"CacheApi\",\"LibName\":\"cache\"," +
            "\"EntryFiles\":[\"api.h\"],\"OutputPath\":\"generated\",\"ImportType\":\"DllImport\"," +
            "\"GenerateExtensions\":false,\"MergeGeneratedFilesToSingleFile\":true}");
        try
        {
            CsCodeGenerator first = CsCodeGenerator.Create(configPath);
            Assert.True(first.GenerateConfigured());
            Assert.False(first.lastResult!.cacheHit);
            string firstKey = Assert.IsType<string>(first.lastResult.cacheKey);
            string bindings = Path.Combine(temp, "generated", "Bindings.cs");
            File.WriteAllText(bindings, "corrupted");

            CsCodeGenerator second = CsCodeGenerator.Create(configPath);
            Assert.True(second.GenerateConfigured());
            Assert.True(second.lastResult!.cacheHit);
            Assert.NotNull(second.lastResult.module);
            Assert.Contains("CacheFirstNative", File.ReadAllText(bindings), StringComparison.Ordinal);

            File.WriteAllText(header, "int cache_second(void);\n");
            CsCodeGenerator third = CsCodeGenerator.Create(configPath);
            Assert.True(third.GenerateConfigured());
            Assert.False(third.lastResult!.cacheHit);
            Assert.NotEqual(firstKey, third.lastResult.cacheKey);
            Assert.Contains("CacheSecondNative", File.ReadAllText(bindings), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_UnsquashedForwardTypedef_ShouldNotEmitVoidAlias()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-forward-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "forward.h");
        string output = Path.Combine(temp, "generated");
        File.WriteAllText(header, "typedef struct Forward Forward; typedef void Opaque; void use_forward(Forward* value, Opaque* opaque);");
        CsCodeGeneratorConfig config = new()
        {
            @namespace = "BGCS.Tests.Generated",
            apiName = "ForwardApi",
            libName = "forward",
            autoSquashTypedef = false,
            mergeGeneratedFilesToSingleFile = true,
            generateExtensions = false,
            importType = ImportType.DllImport
        };

        try
        {
            Assert.True(new CsCodeGenerator(config).Generate(header, output));
            string bindings = File.ReadAllText(Path.Combine(output, "Bindings.cs"));
            Assert.DoesNotContain("using Forward = void;", bindings);
            Assert.Contains("partial struct Forward", bindings);
            Assert.DoesNotContain("using Opaque = void;", bindings);
            Assert.Contains("partial struct Opaque", bindings);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    private sealed class TestGenerator : CsCodeGenerator
    {
        internal TestGenerator(CsCodeGeneratorConfig config) : base(config)
        {
        }

        internal CppParserOptions GetParserOptions() => PrepareSettings();
    }
}

public sealed class TestConfigPlugin : IBindingPlugin
{
    public string id => "bgcs.tests.config-plugin";
    public string version => "1.0.0";
    public int contractVersion => BindingPluginContract.C_CURRENT_VERSION;

    public void Configure(IBindingPluginHost host) =>
        host.Register<ICacheFingerprintProvider>("configured-plugin", new Service());

    private sealed class Service : ICacheFingerprintProvider
    {
        public string GetCacheFingerprint() => "ready";
    }
}
