using System;
using System.IO;
using BGCS.Configuration;
using Xunit;

namespace BGCS.Tests;

public class ConfigComposerBaseConfigPathTests
{
    [Fact]
    public void BaseConfig_FileUrlRelativePath_ShouldResolveAgainstConfigFileDirectory()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "bgcs-config-composer-" + Guid.NewGuid().ToString("N"));
        string configDir = Path.Combine(tempRoot, "configs");
        string otherDir = Path.Combine(tempRoot, "other");
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(otherDir);

        string basePath = Path.Combine(configDir, "base.json");
        string mainPath = Path.Combine(configDir, "main.json");

        File.WriteAllText(basePath,
            """
            {
              "namespace": "Expected.FromBase",
              "apiName": "NativeApi"
            }
            """);

        File.WriteAllText(mainPath,
            """
            {
              "baseConfig": {
                "url": "file://base.json"
              }
            }
            """);

        string previousCwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = otherDir;

            CsCodeGeneratorConfig config = new BGCS.Configuration.ConfigLoader().Load(mainPath);

            Assert.Equal("Expected.FromBase", config.@namespace);
        }
        finally
        {
            Environment.CurrentDirectory = previousCwd;
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }
}
