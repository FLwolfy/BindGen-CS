using System;
using System.IO;
using BGCS.Core;
using BGCS.Cpp2C;
using BGCS.Cpp2C.Metadata;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public class Cpp2CMetadataFlowTests
{
    [Fact]
    public void CopyFrom_Metadata_ShouldBeAppliedToGenerationSteps()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header, "class Demo { public: int Add(int a, int b); };");

        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());
            ProbeStep step = new(generator, new Cpp2CGeneratorConfig());
            generator.AddGenerationStep(step);

            generator.CopyFrom(new Cpp2CGeneratorMetadata());
            generator.CopyFrom(new Cpp2CGeneratorMetadata());

            generator.Generate(header, output);

            Assert.Equal(2, step.CopyFromCalls);
            Assert.Equal(1, step.ConfigureCalls);
            Assert.Equal(1, step.GenerateCalls);
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

        public int ConfigureCalls { get; private set; }

        public int GenerateCalls { get; private set; }

        public int CopyFromCalls { get; private set; }

        public override string Name => "ProbeStep";

        public override void Configure(Cpp2CGeneratorConfig config)
        {
            ConfigureCalls++;
        }

        public override void Generate(FileSet files, ParseResult result, string outputPath, Cpp2CGeneratorConfig config, Cpp2CGeneratorMetadata metadata)
        {
            GenerateCalls++;
        }

        public override void CopyToMetadata(Cpp2CGeneratorMetadata metadata)
        {
        }

        public override void CopyFromMetadata(Cpp2CGeneratorMetadata metadata)
        {
            CopyFromCalls++;
        }

        public override void Reset()
        {
        }
    }
}
