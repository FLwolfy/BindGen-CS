using System;
using System.IO;
using System.Text.Json;
using BGCS.Configuration;
using BGCS.Core.Extensibility;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.Tests;

public class CsCodeGeneratorConfigTests
{
    [Fact]
    public void Constructor_CollectionsShouldBeInitialized()
    {
        CsCodeGeneratorConfig cfg = new();

        Assert.NotNull(cfg.EntryFiles);
        Assert.NotNull(cfg.AllowedHeaders);
        Assert.NotNull(cfg.IncludeFolders);
        Assert.NotNull(cfg.SystemIncludeFolders);
        Assert.NotNull(cfg.Defines);
        Assert.NotNull(cfg.AdditionalArguments);
        Assert.NotNull(cfg.TypeMappings);
        Assert.NotNull(cfg.NameMappings);
        Assert.NotNull(cfg.Keywords);
        Assert.NotNull(cfg.FunctionMappings);
        Assert.NotNull(cfg.ArrayMappings);
        Assert.NotNull(cfg.PluginAssemblies);
    }

    [Fact]
    public void Validator_ShouldRejectInvalidCacheAndPluginSettings()
    {
        CsCodeGeneratorConfig config = new()
        {
            Namespace = "Test.Generated",
            ApiName = "TestApi",
            LibName = "test",
            CacheDirectory = " ",
            PluginAssemblies = [""]
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => ConfigValidator.Validate(config));

        Assert.Contains("CacheDirectory", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PluginAssemblies", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PresetResolver_ShouldComposeNamedDefaultsAndRejectUnknownNames()
    {
        CsCodeGeneratorConfig config = new() { Preset = "windows-c,c-library,opaque-callbacks,function-table" };

        BGCS.Configuration.PresetResolver.Default.Apply(config);

        Assert.Equal(CppParserKind.C, config.ParserKind);
        Assert.Equal(ImportType.FunctionTable, config.ImportType);
        Assert.True(config.UseCustomContext);
        Assert.True(config.WrapPointersAsHandle);
        Assert.True(config.DelegatesAsVoidPointer);
        Assert.False(config.ParseMacros);
        CsCodeGeneratorConfig invalid = new() { Preset = "missing-preset" };
        Assert.Throws<InvalidOperationException>(() => BGCS.Configuration.PresetResolver.Default.Apply(invalid));
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
              "Preset": "c-library",
              "Namespace": "Test.Generated",
              "ApiName": "TestApi",
              "LibName": "test",
              "AutoSquashTypedef": true,
              "ParseMacros": true,
              "GenerateExtensions": true,
              "ImportType": "LibraryImport"
            }
            """);

        try
        {
            CsCodeGeneratorConfig config = new ConfigLoader().Load(path);

            Assert.True(config.AutoSquashTypedef);
            Assert.True(config.ParseMacros);
            Assert.True(config.GenerateExtensions);
            Assert.Equal(ImportType.LibraryImport, config.ImportType);
            Assert.Equal(CppParserKind.C, config.ParserKind);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void ConfigLoader_FutureConfigVersion_ShouldFailBeforeGeneration()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-config-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string path = Path.Combine(temp, "bindings.json");
        File.WriteAllText(path,
            "{\"ConfigVersion\":999,\"Namespace\":\"Test.Generated\",\"ApiName\":\"TestApi\",\"LibName\":\"test\",\"EntryFiles\":[]}");

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new ConfigLoader().Load(path));

            Assert.Contains("ConfigVersion 999", exception.Message, StringComparison.Ordinal);
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

            BindingPluginService<ICacheFingerprintProvider> service = Assert.Single(config.Plugins.GetServices<ICacheFingerprintProvider>());
            Assert.Equal("configured-plugin", service.Id);
            Assert.Equal("ready", service.Service.GetCacheFingerprint());
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
            Assert.False(first.LastResult!.CacheHit);

            CsCodeGenerator second = CsCodeGenerator.Create(path);
            Assert.True(second.GenerateConfigured());
            Assert.True(second.LastResult!.CacheHit);
            Assert.Equal(first.LastResult.CacheKey, second.LastResult.CacheKey);
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
        File.WriteAllText(basePath, "{\"Preset\":\"c-library\",\"Namespace\":\"Base.Namespace\",\"ApiName\":\"TestApi\",\"LibName\":\"test\"}");
        File.WriteAllText(childPath,
            """
            {
              "BaseConfig": { "Url": "file://base.json" },
              "AutoSquashTypedef": true,
              "ParseMacros": true
            }
            """);

        try
        {
            CsCodeGeneratorConfig config = new ConfigLoader().Load(childPath);

            Assert.Equal("Base.Namespace", config.Namespace);
            Assert.Equal("c-library", config.Preset);
            Assert.True(config.AutoSquashTypedef);
            Assert.True(config.ParseMacros);
            Assert.False(config.GenerateExtensions);
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
            TargetPlatform = CppTargetPlatform.Windows,
            TargetArchitecture = CppTargetArchitecture.X86,
            TargetAbi = CppTargetAbi.Msvc
        };
        TestGenerator generator = new(config);

        CppParserOptions options = generator.GetParserOptions();

        Assert.Equal(CppTargetCpu.X86, options.TargetCpu);
        Assert.Contains("_WIN32=1", options.Defines);
        Assert.Contains("_M_IX86=600", options.Defines);
        Assert.DoesNotContain("_WIN64=1", options.Defines);
    }

    [Fact]
    public void Merge_WhenFlagsProvided_ShouldMergeOnlySelectedCollections()
    {
        CsCodeGeneratorConfig cfg = new();
        CsCodeGeneratorConfig baseCfg = new();

        cfg.IncludeFolders.Add("a");
        baseCfg.IncludeFolders.Add("b");
        baseCfg.Defines.Add("D1");

        cfg.Merge(baseCfg, MergeOptions.IncludeFolders);

        Assert.Contains("a", cfg.IncludeFolders);
        Assert.Contains("b", cfg.IncludeFolders);
        Assert.Empty(cfg.Defines);
    }

    [Fact]
    public void Merge_WhenNoFlags_ShouldNotModifyCollections()
    {
        CsCodeGeneratorConfig cfg = new();
        CsCodeGeneratorConfig baseCfg = new();
        baseCfg.IncludeFolders.Add("from-base");

        cfg.Merge(baseCfg, MergeOptions.None);

        Assert.Empty(cfg.IncludeFolders);
    }

    [Fact]
    public void Load_WhenFileMissing_ShouldCreateFileAndReturnConfig()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string configPath = Path.Combine(temp, "config.json");

        try
        {
            Assert.False(File.Exists(configPath));
            var cfg = CsCodeGeneratorConfig.Load(configPath);

            Assert.NotNull(cfg);
            Assert.True(File.Exists(configPath));
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
        const string source = "{\"ApiName\":\"Test\",\"ExtensionSetting\":42}";
        File.WriteAllText(configPath, source);

        try
        {
            CsCodeGeneratorConfig cfg = CsCodeGeneratorConfig.Load(configPath);

            Assert.Equal("Test", cfg.ApiName);
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
              "Namespace": "Configured.Generated",
              "ApiName": "ConfiguredApi",
              "LibName": "configured",
              "EntryFiles": ["api.h"],
              "IncludeTransitivelyReferencedHeaders": true,
              "OutputPath": "generated",
              "ImportType": "DllImport",
              "GenerateExtensions": false,
              "MergeGeneratedFilesToSingleFile": true
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
            Assert.False(first.LastResult!.CacheHit);
            string firstKey = Assert.IsType<string>(first.LastResult.CacheKey);
            string bindings = Path.Combine(temp, "generated", "Bindings.cs");
            File.WriteAllText(bindings, "corrupted");

            CsCodeGenerator second = CsCodeGenerator.Create(configPath);
            Assert.True(second.GenerateConfigured());
            Assert.True(second.LastResult!.CacheHit);
            Assert.NotNull(second.LastResult.Module);
            Assert.Contains("CacheFirstNative", File.ReadAllText(bindings), StringComparison.Ordinal);

            File.WriteAllText(header, "int cache_second(void);\n");
            CsCodeGenerator third = CsCodeGenerator.Create(configPath);
            Assert.True(third.GenerateConfigured());
            Assert.False(third.LastResult!.CacheHit);
            Assert.NotEqual(firstKey, third.LastResult.CacheKey);
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
            Namespace = "BGCS.Tests.Generated",
            ApiName = "ForwardApi",
            LibName = "forward",
            AutoSquashTypedef = false,
            MergeGeneratedFilesToSingleFile = true,
            GenerateExtensions = false,
            ImportType = ImportType.DllImport
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
    public string Id => "bgcs.tests.config-plugin";
    public string Version => "1.0.0";
    public int ContractVersion => BindingPluginContract.CurrentVersion;

    public void Configure(IBindingPluginHost host) =>
        host.Register<ICacheFingerprintProvider>("configured-plugin", new Service());

    private sealed class Service : ICacheFingerprintProvider
    {
        public string GetCacheFingerprint() => "ready";
    }
}
