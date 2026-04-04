using System;
using System.IO;
using BGCS.Core;
using BGCS.Core.Logging;
using BGCS.Cpp2C.Metadata;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public class Cpp2CDefaultPipelineTests
{
    [Fact]
    public void Generate_WithoutManualSteps_ShouldApplyDefaultPipeline()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-default-auto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            """
            enum Mode
            {
                A = 1
            };

            class Counter
            {
            public:
                virtual int Add(int a, int b);
            };
            """);

        try
        {
            Cpp2CGeneratorConfig cfg = new();
            Cpp2CCodeGenerator gen = new(cfg);
            gen.Generate(header, output);

            Assert.DoesNotContain(gen.Messages, x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);
            Assert.True(File.Exists(Path.Combine(output, "include", "Classes.h")));
            Assert.True(File.Exists(Path.Combine(output, "include", "enums.h")));
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
    public void Generate_WithManualPipeline_ShouldNotAutoAppendDefaults()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-default-manual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header, "class Counter { public: virtual int Add(int a, int b); };");

        try
        {
            Cpp2CGeneratorConfig cfg = new();
            Cpp2CCodeGenerator gen = new(cfg);
            gen.AddGenerationStep(new ProbeStep(gen, cfg));
            gen.Generate(header, output);

            Assert.DoesNotContain(gen.Messages, x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);
            Assert.True(File.Exists(Path.Combine(output, "probe.txt")));
            Assert.False(File.Exists(Path.Combine(output, "include", "Classes.h")));
            Assert.False(File.Exists(Path.Combine(output, "include", "enums.h")));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    private sealed class ProbeStep : GenerationStep
    {
        public ProbeStep(Cpp2CCodeGenerator generator, Cpp2CGeneratorConfig config) : base(generator, config)
        {
        }

        public override string Name => "ProbeStep";

        public override void Configure(Cpp2CGeneratorConfig config)
        {
        }

        public override void Generate(FileSet files, ParseResult result, string outputPath, Cpp2CGeneratorConfig config, Cpp2CGeneratorMetadata metadata)
        {
            File.WriteAllText(Path.Combine(outputPath, "probe.txt"), "ok");
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
