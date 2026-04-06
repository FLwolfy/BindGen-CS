using System;
using System.IO;
using Xunit;

namespace BGCS.Tests;

public class ConfigComposerTests
{
    [Fact]
    public void Compose_WithBaseConfigFile_ShouldMergeBaseAndOverrideValues()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-config-compose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string basePath = Path.Combine(temp, "base.json");
        string childPath = Path.Combine(temp, "child.json");

        File.WriteAllText(basePath,
            """
            {
              "Namespace": "Base.Namespace",
              "ApiName": "BaseApi",
              "LibName": "base",
              "IncludeFolders": [ "base/include" ],
              "Defines": [ "BASE_DEFINE" ],
              "GenerateExtensions": true
            }
            """);

        File.WriteAllText(childPath,
            """
            {
              "BaseConfig": {
                "Url": "file://base.json"
              },
              "ApiName": "ChildApi",
              "IncludeFolders": [ "child/include" ],
              "GenerateExtensions": false
            }
            """);

        string previousCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = temp;
        try
        {
            CsCodeGeneratorConfig cfg = CsCodeGeneratorConfig.Load(childPath, new ConfigComposer());

            Assert.Equal("Base.Namespace", cfg.Namespace);
            Assert.Equal("ChildApi", cfg.ApiName);
            Assert.Equal("base", cfg.LibName);
            Assert.Contains("base/include", cfg.IncludeFolders);
            Assert.Contains("child/include", cfg.IncludeFolders);
            Assert.Contains("BASE_DEFINE", cfg.Defines);
            Assert.False(cfg.GenerateExtensions);
        }
        finally
        {
            Environment.CurrentDirectory = previousCwd;
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    [Fact]
    public void Compose_WithIgnoredProperties_ShouldExcludeSpecifiedBaseProperties()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-config-ignore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string basePath = Path.Combine(temp, "base.json");
        string childPath = Path.Combine(temp, "child.json");

        File.WriteAllText(basePath,
            """
            {
              "Namespace": "Base.Namespace",
              "Defines": [ "BASE_DEFINE" ]
            }
            """);

        File.WriteAllText(childPath,
            """
            {
              "BaseConfig": {
                "Url": "file://base.json",
                "IgnoredProperties": [ "Defines" ]
              }
            }
            """);

        string previousCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = temp;
        try
        {
            CsCodeGeneratorConfig cfg = CsCodeGeneratorConfig.Load(childPath, new ConfigComposer());

            Assert.Equal("Base.Namespace", cfg.Namespace);
            Assert.DoesNotContain("BASE_DEFINE", cfg.Defines);
        }
        finally
        {
            Environment.CurrentDirectory = previousCwd;
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
