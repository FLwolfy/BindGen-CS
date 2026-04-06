using System;
using System.IO;
using BGCS.Core.Logging;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public class Cpp2CEndToEndWorkflowTests
{
    [Fact]
    public void LoadConfigWithBaseFile_ThenGenerate_ShouldProduceExpectedOutputStructure()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string baseConfigPath = Path.Combine(temp, "config.base.json");
        string configPath = Path.Combine(temp, "config.json");
        string headerPath = Path.Combine(temp, "demo.hpp");
        string outputPath = Path.Combine(temp, "out");

        File.WriteAllText(baseConfigPath,
            """
            {
              "NamePrefix": "BASE_",
              "LogLevel": "Warning",
              "CppLogLevel": "Error"
            }
            """);

        File.WriteAllText(configPath,
            """
            {
              "BaseConfig": {
                "Url": "file://config.base.json"
              },
              "NamePrefix": "CHILD_"
            }
            """);

        File.WriteAllText(headerPath,
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

        string previousCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = temp;

        try
        {
            Cpp2CGeneratorConfig cfg = Cpp2CGeneratorConfig.Load(configPath);
            Assert.Equal("CHILD_", cfg.NamePrefix);
            Assert.Equal(LogSeverity.Warning, cfg.LogLevel);
            Assert.Equal(LogSeverity.Error, cfg.CppLogLevel);

            Cpp2CCodeGenerator generator = new(cfg);
            generator.Generate(headerPath, outputPath);

            Assert.DoesNotContain(generator.Messages, m => m.Severtiy is LogSeverity.Error or LogSeverity.Critical);
            Assert.True(File.Exists(Path.Combine(outputPath, "include", "Classes.h")));
            Assert.True(File.Exists(Path.Combine(outputPath, "include", "enums.h")));
            Assert.True(File.Exists(Path.Combine(outputPath, "src", "Classes.cpp")));
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
