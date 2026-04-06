using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Core.Logging;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public class Cpp2CGeneratorConfigOptionEffectTests
{
    [Fact]
    public void AllOptions_ShouldRoundTripWithExpectedValues()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-cfg-explicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string configPath = Path.Combine(tempDir, "config.json");
        File.WriteAllText(Path.Combine(tempDir, "base.json"), "{}");

        string oldCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = tempDir;

        try
        {
            var cfg = new Cpp2CGeneratorConfig
            {
                BaseConfig = new BaseConfig { Url = "file://base.json", IgnoredProperties = ["AdditionalArguments"] },
                LogLevel = LogSeverity.Debug,
                CppLogLevel = LogSeverity.Information,
                NamePrefix = "PrefixX_",
                IncludeFolders = new List<string> { "inc_x" },
                SystemIncludeFolders = new List<string> { "sys_inc_x" },
                Defines = new List<string> { "DEF_X=1" },
                AdditionalArguments = new List<string> { "-std=c++20" }
            };

            cfg.Save(configPath);
            Cpp2CGeneratorConfig loaded = Cpp2CGeneratorConfig.Load(configPath, NoopComposer.Instance);

            Assert.NotNull(loaded.BaseConfig);
            Assert.Equal("file://base.json", loaded.BaseConfig!.Url);
            Assert.Contains("AdditionalArguments", loaded.BaseConfig.IgnoredProperties);
            Assert.Equal(LogSeverity.Debug, loaded.LogLevel);
            Assert.Equal(LogSeverity.Information, loaded.CppLogLevel);
            Assert.Equal("PrefixX_", loaded.NamePrefix);
            Assert.Contains("inc_x", loaded.IncludeFolders);
            Assert.Contains("sys_inc_x", loaded.SystemIncludeFolders);
            Assert.Contains("DEF_X=1", loaded.Defines);
            Assert.Contains("-std=c++20", loaded.AdditionalArguments);
        }
        finally
        {
            Environment.CurrentDirectory = oldCwd;
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private sealed class NoopComposer : IConfigComposer
    {
        public static readonly NoopComposer Instance = new();

        public void Compose(ref Cpp2CGeneratorConfig config)
        {
        }
    }
}
