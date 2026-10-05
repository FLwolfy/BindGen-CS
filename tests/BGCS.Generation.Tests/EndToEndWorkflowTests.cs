using System;
using System.IO;
using BGCS.Configuration;
using BGCS.Facade;
using Xunit;

namespace BGCS.Tests;

public class EndToEndWorkflowTests
{
    [Fact]
    public void ConfiguredGeneration_OutputOverrideUsesTheSharedPipelineAndRuntimeNamespace()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string configPath = Path.Combine(temp, "config.json");
        string headerPath = Path.Combine(temp, "demo.h");
        string cliRoot = Path.Combine(temp, "cli-root");
        string relativeOut = "generated";

        File.WriteAllText(headerPath, "int demo_add(int a, int b);");

        CsCodeGeneratorConfig cfg = new()
        {
            entryFiles = ["demo.h"],
            apiName = "DemoApi",
            @namespace = "BGCS.Tests.E2E",
            libName = "demo",
            generateExtensions = false,
            mergeGeneratedFilesToSingleFile = true,
            generateRuntimeSource = true,
            runtimeNamespace = "Custom.Runtime",
            importType = ImportType.DllImport
        };

        cfg.Save(configPath);

        try
        {
            CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
            Assert.True(generator.GenerateConfigured(Path.Combine(cliRoot, relativeOut)));

            string actualOut = Path.Combine(cliRoot, relativeOut);
            string bindingsPath = Path.Combine(actualOut, "Bindings.cs");
            string runtimePath = Path.Combine(actualOut, "Runtime.cs");

            Assert.True(File.Exists(bindingsPath), $"Expected generated bindings at: {bindingsPath}");
            Assert.True(File.Exists(runtimePath), $"Expected generated runtime source at: {runtimePath}");

            string bindings = File.ReadAllText(bindingsPath);
            string runtime = File.ReadAllText(runtimePath);

            Assert.Contains("DemoAddNative", bindings);
            Assert.Contains("using Custom.Runtime;", bindings);
            Assert.Contains("namespace Custom.Runtime", runtime);
            Assert.Contains("#if !BGCS_RUNTIME_EXTERNAL", runtime);
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
