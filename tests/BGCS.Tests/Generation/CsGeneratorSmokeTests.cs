using System;
using System.IO;
using System.Linq;
using BGCS.Core.Logging;
using Xunit;

namespace BGCS.Tests;

public class CsGeneratorSmokeTests
{
    [Fact]
    public void Generate_MinimalHeader_ShouldNotReportErrors()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string header = Path.Combine(temp, "sample.h");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header, "typedef struct MyStruct { int a; } MyStruct;\nvoid test_fn(int value);");

        try
        {
            CsCodeGeneratorConfig cfg = new()
            {
                ApiName = "TestApi",
                Namespace = "Test.Generated",
                LibName = "test",
                GenerateExtensions = false
            };

            CsCodeGenerator gen = new(cfg);
            var ok = gen.Generate(header, output);

            Assert.True(ok);
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
    public void Generate_ConfigWithNullCollections_ShouldNotThrowNullReference()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-smoke-nullcfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string header = Path.Combine(temp, "sample.h");
        string output = Path.Combine(temp, "out");
        string configPath = Path.Combine(temp, "config.json");
        File.WriteAllText(header, "typedef struct MyStruct { int a; } MyStruct;\nvoid test_fn(int value);");
        File.WriteAllText(configPath,
            "{\n" +
            "  \"ApiName\": \"TestApi\",\n" +
            "  \"Namespace\": \"Test.Generated\",\n" +
            "  \"LibName\": \"test\",\n" +
            "  \"GenerateExtensions\": false,\n" +
            "  \"AdditionalArguments\": null,\n" +
            "  \"IncludeFolders\": null,\n" +
            "  \"SystemIncludeFolders\": null,\n" +
            "  \"Defines\": null\n" +
            "}");

        try
        {
            CsCodeGeneratorConfig cfg = CsCodeGeneratorConfig.Load(configPath);
            CsCodeGenerator gen = new(cfg);

            Exception? ex = Record.Exception(() => gen.Generate(header, output));
            Assert.Null(ex);
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
    public void Generate_MergeGeneratedFilesToSingleFile_ShouldCreateSingleBindingsFile()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-smoke-singlefile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string header = Path.Combine(temp, "sample.h");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header, "typedef struct MyStruct { int a; } MyStruct;\nvoid test_fn(int value);");

        try
        {
            CsCodeGeneratorConfig cfg = new()
            {
                ApiName = "TestApi",
                Namespace = "Test.Generated",
                LibName = "test",
                GenerateExtensions = false,
                MergeGeneratedFilesToSingleFile = true,
                SingleFileOutputName = "AllBindings.cs"
            };

            CsCodeGenerator gen = new(cfg);
            var ok = gen.Generate(header, output);

            Assert.True(ok);
            Assert.DoesNotContain(gen.Messages, x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);

            string mergedPath = Path.Combine(output, "AllBindings.cs");
            Assert.True(File.Exists(mergedPath));

            string merged = File.ReadAllText(mergedPath);
            Assert.Contains("TestFnNative", merged);
            Assert.Contains("partial struct MyStruct", merged);

            string[] csFiles = Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories);
            Assert.Single(csFiles);
            Assert.Equal(mergedPath, csFiles[0]);
            Assert.Empty(Directory.GetDirectories(output, "*", SearchOption.AllDirectories));
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
