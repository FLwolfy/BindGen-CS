using System;
using System.IO;
using System.Linq;
using BGCS.Core;
using BGCS.Core.Logging;
using BGCS.Cpp2C.Metadata;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public class Cpp2CGeneratorTests
{
    [Fact]
    public void Config_DefaultCollections_ShouldBeInitialized()
    {
        Cpp2CGeneratorConfig cfg = new();

        Assert.NotNull(cfg.IncludeFolders);
        Assert.NotNull(cfg.SystemIncludeFolders);
        Assert.NotNull(cfg.Defines);
        Assert.NotNull(cfg.AdditionalArguments);
    }

    [Fact]
    public void Generate_MinimalClassHeader_ShouldNotReportErrors()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header, "class Demo { public: int Add(int a, int b); };");

        try
        {
            Cpp2CGeneratorConfig cfg = new();
            Cpp2CCodeGenerator gen = new(cfg);
            gen.Generate(header, output);

            Assert.DoesNotContain(gen.Messages, x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);
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
    public void Config_SaveAndLoad_ShouldRoundTripValues()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string path = Path.Combine(temp, "cfg.json");

        try
        {
            Cpp2CGeneratorConfig cfg = new();
            cfg.IncludeFolders.Add("include-a");
            cfg.Defines.Add("DEF_A=1");
            cfg.NamePrefix = "Prefix";

            cfg.Save(path);
            var loaded = Cpp2CGeneratorConfig.Load(path);

            Assert.Contains("include-a", loaded.IncludeFolders);
            Assert.Contains("DEF_A=1", loaded.Defines);
            Assert.Equal("Prefix", loaded.NamePrefix);
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
    public void GenerationStep_AddGetOverwrite_ShouldWork()
    {
        Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

        var a = new DummyStepA(generator, new Cpp2CGeneratorConfig());
        var b = new DummyStepB(generator, new Cpp2CGeneratorConfig());

        generator.AddGenerationStep(a);
        Assert.Same(a, generator.GetGenerationStep<DummyStepA>());

        generator.OverwriteGenerationStep<DummyStepA>(b);
        Assert.Same(b, generator.GetGenerationStep<DummyStepB>());
        Assert.Throws<InvalidOperationException>(() => generator.GetGenerationStep<DummyStepA>());
    }

    [Fact]
    public void GetGenerationStep_WhenNotFound_ShouldThrow()
    {
        Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());

        Assert.Throws<InvalidOperationException>(() => generator.GetGenerationStep<DummyStepA>());
    }

    private sealed class DummyStepA : GenerationStep
    {
        public DummyStepA(Cpp2CCodeGenerator generator, Cpp2CGeneratorConfig config) : base(generator, config)
        {
        }

        public override string Name => "DummyA";

        public override void Configure(Cpp2CGeneratorConfig config)
        {
        }

        public override void Generate(FileSet files, ParseResult result, string outputPath, Cpp2CGeneratorConfig config, Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void CopyToMetadata(Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void CopyFromMetadata(Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void Reset()
        {
        }
    }

    private sealed class DummyStepB : GenerationStep
    {
        public DummyStepB(Cpp2CCodeGenerator generator, Cpp2CGeneratorConfig config) : base(generator, config)
        {
        }

        public override string Name => "DummyB";

        public override void Configure(Cpp2CGeneratorConfig config)
        {
        }

        public override void Generate(FileSet files, ParseResult result, string outputPath, Cpp2CGeneratorConfig config, Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void CopyToMetadata(Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void CopyFromMetadata(Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void Reset()
        {
        }
    }
}
