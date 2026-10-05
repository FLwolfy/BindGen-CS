using System;
using System.IO;
using BGCS.Core.Logging;
using BGCS.Cpp2C.Configuration;
using BGCS.Cpp2C.Facade;
using BGCS.Intermediate;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class GenerationLifetimeTests
{
    [Fact]
    public void RepeatedAttempts_DoNotRetainParserFailuresOrPriorSuccessfulResults()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-bridge-lifetime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string header = Path.Combine(directory, "api.hpp");
            var config = new Cpp2CGeneratorConfig
            {
                entryFiles = [header],
                outputPath = Path.Combine(directory, "Generated"),
                cppLogLevel = LogSeverity.Critical,
                logLevel = LogSeverity.Critical
            };
            var generator = new Cpp2CCodeGenerator(config);
            File.WriteAllText(header, "class Broken { int value(;");
            generator.GenerateConfigured();
            Assert.False(generator.lastResult!.success);
            Assert.Contains(generator.lastResult.diagnostics, diagnostic => diagnostic.severity >= BindingDiagnosticSeverity.Error);

            File.WriteAllText(header, "class Value { public: int get() const; };");
            generator.GenerateConfigured();
            Assert.True(generator.lastResult!.success);
            Assert.DoesNotContain(generator.messages, diagnostic => diagnostic.severity >= LogSeverity.Error);
            Assert.DoesNotContain(generator.lastResult.diagnostics, diagnostic => diagnostic.severity >= BindingDiagnosticSeverity.Error);

            config.entryFiles.Clear();
            Assert.Throws<InvalidOperationException>(() => generator.GenerateConfigured());
            Assert.Null(generator.lastResult);
            Assert.Empty(generator.messages);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
