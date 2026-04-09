using System;
using System.IO;
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
              "Namespace": "Expected.FromBase"
            }
            """);

        File.WriteAllText(mainPath,
            """
            {
              "BaseConfig": {
                "Url": "file://base.json"
              }
            }
            """);

        string previousCwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = otherDir;

            CsCodeGeneratorConfig config = CsCodeGeneratorConfig.Load(mainPath, new ConfigComposer());

            Assert.Equal("Expected.FromBase", config.Namespace);
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
