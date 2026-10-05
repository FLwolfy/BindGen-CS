using System;
using System.IO;
using BGCS.Core.Logging;
using BGCS.Cpp2C.Configuration;
using BGCS.Cpp2C.Facade;
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

            Assert.DoesNotContain(gen.messages, x => x.severity is LogSeverity.Error or LogSeverity.Critical);
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

}
