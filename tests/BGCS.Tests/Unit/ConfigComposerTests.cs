using System;
using System.IO;
using BGCS.Configuration;
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
              "namespace": "Base.Namespace",
              "apiName": "BaseApi",
              "libName": "base",
              "includeFolders": [
                "base/include"
              ],
              "defines": [
                "BASE_DEFINE"
              ],
              "generateExtensions": true
            }
            """);

        File.WriteAllText(childPath,
            """
            {
              "baseConfig": {
                "url": "file://base.json"
              },
              "apiName": "ChildApi",
              "includeFolders": [
                "child/include"
              ],
              "generateExtensions": false
            }
            """);

        string previousCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = temp;
        try
        {
            CsCodeGeneratorConfig cfg = new BGCS.Configuration.ConfigLoader().Load(childPath);

            Assert.Equal("Base.Namespace", cfg.@namespace);
            Assert.Equal("ChildApi", cfg.apiName);
            Assert.Equal("base", cfg.libName);
            Assert.Contains("base/include", cfg.includeFolders);
            Assert.Contains("child/include", cfg.includeFolders);
            Assert.Contains("BASE_DEFINE", cfg.defines);
            Assert.False(cfg.generateExtensions);
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
              "namespace": "Base.Namespace",
              "apiName": "BaseApi",
              "defines": [
                "BASE_DEFINE"
              ]
            }
            """);

        File.WriteAllText(childPath,
            """
            {
              "baseConfig": {
                "url": "file://base.json",
                "ignoredProperties": [
                  "defines"
                ]
              }
            }
            """);

        string previousCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = temp;
        try
        {
            CsCodeGeneratorConfig cfg = new BGCS.Configuration.ConfigLoader().Load(childPath);

            Assert.Equal("Base.Namespace", cfg.@namespace);
            Assert.DoesNotContain("BASE_DEFINE", cfg.defines);
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
    public void Compose_WithCircularBaseConfigs_ShouldFailClearly()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-config-cycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string firstPath = Path.Combine(temp, "first.json");
        string secondPath = Path.Combine(temp, "second.json");
        File.WriteAllText(firstPath, "{\"baseConfig\":{\"url\":\"file://second.json\"}}");
        File.WriteAllText(secondPath, "{\"baseConfig\":{\"url\":\"file://first.json\"}}");

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new BGCS.Configuration.ConfigLoader().Load(firstPath));

            Assert.Contains("Circular BaseConfig", exception.Message);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
