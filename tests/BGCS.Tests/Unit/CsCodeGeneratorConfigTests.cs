using System;
using System.IO;
using Xunit;

namespace BGCS.Tests;

public class CsCodeGeneratorConfigTests
{
    [Fact]
    public void Constructor_CollectionsShouldBeInitialized()
    {
        CsCodeGeneratorConfig cfg = new();

        Assert.NotNull(cfg.IncludeFolders);
        Assert.NotNull(cfg.SystemIncludeFolders);
        Assert.NotNull(cfg.Defines);
        Assert.NotNull(cfg.AdditionalArguments);
        Assert.NotNull(cfg.TypeMappings);
        Assert.NotNull(cfg.NameMappings);
        Assert.NotNull(cfg.Keywords);
        Assert.NotNull(cfg.FunctionMappings);
        Assert.NotNull(cfg.ArrayMappings);
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
}
