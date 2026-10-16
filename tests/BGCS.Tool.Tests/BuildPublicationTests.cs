using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class BuildPublicationTests
{
    [Fact]
    public void ManagedCompilationFailure_PreservesThePreviouslyPublishedBindings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-build-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "native.h"), "int native_value(void);");
            string config = Path.Combine(directory, "bindgen.json");
            WriteConfiguration(config, []);
            using StringWriter error = new();
            Assert.Equal(0, CliInvocation.Run(["generate", config], directory, TextWriter.Null, error));
            string output = Path.Combine(directory, "Generated");
            string[] files = Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories);
            Assert.NotEmpty(files);
            string original = File.ReadAllText(files[0]);
            string broken = Path.Combine(directory, "broken.cs");
            File.WriteAllText(broken, "namespace PublicationRegression; public class Broken { public MissingCompilationReferenceForPublicationRegression Value; }");
            WriteConfiguration(config, ["broken.cs"]);
            int exit = CliInvocation.Run(["build", config], directory, TextWriter.Null, error);
            Assert.NotEqual(0, exit);
            Assert.Equal(original, File.ReadAllText(files[0]));
            Assert.DoesNotContain(Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories),
                path => File.ReadAllText(path).Contains("MissingCompilationReferenceForPublicationRegression", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteConfiguration(
        string path,
        string[] sources
    ) {
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            library = new { id = "native", nativeName = "native" },
            source = new { language = "c", headers = new[] { "native.h" } },
            api = new { @namespace = "PublicationRegression", name = "Api", importMode = "dllImport" },
            output = new { root = "Generated", layout = "singleFile" },
            managedBuild = new { sources }
        }));
    }
}
