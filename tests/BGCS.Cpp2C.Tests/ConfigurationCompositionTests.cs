using System;
using System.IO;
using BGCS.Cpp2C.Configuration;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class ConfigurationCompositionTests
{
    [Fact]
    public void FileCompositionKeepsExplicitDefaultValuesAndClearsInheritance()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-cpp-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "base.json"),
                """{"enableIncrementalCache":false,"languageStandard":"c++17"}""");
            string path = Path.Combine(directory, "child.json");
            File.WriteAllText(path,
                """{"baseConfig":{"url":"file://base.json","ignoredProperties":["languageStandard"]},"enableIncrementalCache":true}""");

            Cpp2CGeneratorConfig config = Cpp2CGeneratorConfig.Load(path);

            Assert.True(config.enableIncrementalCache);
            Assert.Equal("c++23", config.languageStandard);
            Assert.Null(config.baseConfig);
            Assert.Equal(directory, config.configDirectory);
            Assert.Contains("baseConfig", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
