using System;
using System.IO;
using BGCS.Configuration;
using BGCS.Facade;
using Xunit;

namespace BGCS.Tests;

public sealed class BindingGeneratorTests
{
    [Fact]
    public void FacadePublishesStructuredSuccessAndRetainsOutputOnParserFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "BgcsFacade", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "api.h");
            File.WriteAllText(header, "int sample(int value);");
            string config = Path.Combine(root, "bindgen.json");
            new CsCodeGeneratorConfig
            {
                apiName = "Api", @namespace = "Fixture", libName = "fixture", entryFiles = ["api.h"],
                allowedHeaders = ["api.h"], generateRuntimeSource = false, mergeGeneratedFilesToSingleFile = true,
                singleFileOutputName = "Bindings.cs"
            }.Save(config);

            var success = BindingGenerator.Generate(config, "output");
            Assert.True(success.success);
            Assert.NotNull(success.module);
            string output = Assert.Single(success.outputFiles);
            string source = File.ReadAllText(output);
            File.WriteAllText(header, "this is invalid C;");

            var failure = BindingGenerator.Generate(config, "output");

            Assert.False(failure.success);
            Assert.NotEmpty(failure.diagnostics);
            Assert.Empty(failure.outputFiles);
            Assert.Equal(source, File.ReadAllText(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
