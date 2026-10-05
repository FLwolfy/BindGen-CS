using System;
using System.IO;
using System.Text.Json;
using BGCS.Cpp2C.Build;
using BGCS.Cpp2C.Configuration;
using BGCS.Cpp2C.Facade;
using BGCS.Intermediate;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class CppBridgeBuildManifestTests
{
    [Fact]
    public void Generate_EmitsPortableDeterministicBuildContract()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-build-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "GeneratedBridge");
        File.WriteAllText(header, "class Demo { public: int Add(int value); };\n");
        try
        {
            Cpp2CGeneratorConfig config = new()
            {
                nativeLibraryName = "demo_bridge",
                languageStandard = "c++20"
            };
            Cpp2CCodeGenerator generator = new(config);

            generator.Generate(header, output);

            Assert.True(generator.lastResult?.success);
            string manifestPath = Path.Combine(output, "bridge.manifest.json");
            Assert.Contains(manifestPath, generator.lastResult!.outputFiles);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            JsonElement root = document.RootElement;
            Assert.Equal("c++20", root.GetProperty("languageStandard").GetString());
            Assert.Equal("demo_bridge", root.GetProperty("libraryName").GetString());
            Assert.Contains("src/Classes.cpp", root.GetProperty("sourceFiles").ToString(), StringComparison.Ordinal);
            Assert.Contains("include/Classes.h", root.GetProperty("publicHeaderFiles").ToString(), StringComparison.Ordinal);
            Assert.Contains("../sample.hpp", root.GetProperty("originalHeaderFiles").ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(temp.Replace('\\', '/'), File.ReadAllText(manifestPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_RelativeCompilerPath_IsManifestRelativeToConfiguration()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-build-manifest-compiler-" + Guid.NewGuid().ToString("N"));
        string configDirectory = Path.Combine(temp, "config");
        Directory.CreateDirectory(Path.Combine(configDirectory, "toolchain"));
        File.WriteAllText(Path.Combine(configDirectory, "sample.hpp"), "class Demo {};\n");
        File.WriteAllText(Path.Combine(configDirectory, "bridge.json"),
            """
            {
              "entryFiles": [
                "sample.hpp"
              ],
              "compilerPath": "toolchain/clang++",
              "outputPath": "GeneratedBridge"
            }
            """);
        try
        {
            Cpp2CGeneratorConfig config = Cpp2CGeneratorConfig.Load(Path.Combine(configDirectory, "bridge.json"));
            string output = Path.Combine(configDirectory, "GeneratedBridge");
            Directory.CreateDirectory(Path.Combine(output, "src"));
            File.WriteAllText(Path.Combine(output, "src", "bridge.cpp"), "");

            string manifestPath = CppBridgeBuildManifestEmitter.Emit(config,
                [Path.Combine(configDirectory, "sample.hpp")], output);
            CppBridgeBuildManifest manifest = CppBridgeBuildManifestSerializer.Load(manifestPath);

            Assert.Equal("../toolchain/clang++", manifest.compilerPath);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Theory]
    [InlineData("../manifest.json")]
    [InlineData("..\\manifest.json")]
    public void Validator_RejectsManifestPathTraversal(string fileName)
    {
        Cpp2CGeneratorConfig config = new() { buildManifestFileName = fileName };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Cpp2CConfigValidator.Validate(config));

        Assert.Contains("without a directory", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_RejectsInvalidCacheAndPluginSettings()
    {
        Cpp2CGeneratorConfig config = new()
        {
            cacheDirectory = " ",
            pluginAssemblies = [""]
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Cpp2CConfigValidator.Validate(config));

        Assert.Contains("CacheDirectory", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PluginAssemblies", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_RejectsInvalidOptionalCSharpSafetyPolicy()
    {
        Cpp2CGeneratorConfig config = new()
        {
            generateCSharpBindings = true,
            cSharpStrictSafetySeverity = (StrictSafetySeverity)999
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Cpp2CConfigValidator.Validate(config));

        Assert.Contains("CSharpStrictSafetySeverity", exception.Message, StringComparison.Ordinal);
    }
}
