using System;
using System.IO;
using Xunit;

namespace BGCS.Tests;

public class EndToEndWorkflowTests
{
    [Fact]
    public void BatchGenerator_WithConfigFileAndCliOutputOverride_ShouldGenerateBindingsAndRuntimeWithConfiguredNamespace()
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
            ApiName = "DemoApi",
            Namespace = "BGCS.Tests.E2E",
            LibName = "demo",
            GenerateExtensions = false,
            MergeGeneratedFilesToSingleFile = true,
            GenerateRuntimeSource = true,
            RuntimeNamespace = "Custom.Runtime",
            ImportType = ImportType.DllImport
        };

        cfg.Save(configPath);

        try
        {
            BatchGenerator
                .Create()
                .WithArgs(["--output-dir", cliRoot])
                .Setup<CsCodeGenerator>(configPath)
                .Generate(headerPath, relativeOut);

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
