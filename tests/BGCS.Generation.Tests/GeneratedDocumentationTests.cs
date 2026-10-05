using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Configuration;
using BGCS.CppAst.Parsing;
using BGCS.Facade;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class GeneratedDocumentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeDocumentationExemptionIsConfinedToGeneratedFiles(bool singleFile)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-generated-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string header = Path.Combine(directory, "api.h");
            File.WriteAllText(header, "typedef struct NativeValue { int value; } NativeValue; int read_value(int input);");
            var config = new CsCodeGeneratorConfig
            {
                apiName = "NativeApi",
                @namespace = "GeneratedDocumentation",
                libName = "fixture",
                parserKind = CppParserKind.C,
                mergeGeneratedFilesToSingleFile = singleFile,
                singleFileOutputName = "Bindings.cs"
            };
            var generator = new CsCodeGenerator(config);
            Assert.True(generator.Generate(header, Path.Combine(directory, "out")));
            var parseOptions = new CSharpParseOptions(documentationMode: DocumentationMode.Diagnose);
            var trees = new List<SyntaxTree>();
            foreach (string file in generator.lastResult!.outputFiles.Where(static path => path.EndsWith(".cs", StringComparison.Ordinal)))
            {
                string source = File.ReadAllText(file);
                Assert.Contains("#pragma warning disable CS1591", source);
                trees.Add(CSharpSyntaxTree.ParseText(source, parseOptions, file));
            }
            trees.Add(CSharpSyntaxTree.ParseText("""
                public sealed class HandWritten {
                    /// <summary>Checks a required caller value.</summary>
                    /// <param name="wrong">An invalid parameter tag.</param>
                    public void Check(int required) { }
                }
                """, parseOptions, "HandWritten.cs"));
            string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
            IEnumerable<MetadataReference> references = trusted.Split(Path.PathSeparator)
                .Append(typeof(BGCS.Runtime.NativeLibrary).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static path => MetadataReference.CreateFromFile(path));
            CSharpCompilation compilation = CSharpCompilation.Create("DocumentationBoundary", trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            using var assembly = new MemoryStream();
            using var documentation = new MemoryStream();
            var emitted = compilation.Emit(assembly, xmlDocumentationStream: documentation);

            Assert.DoesNotContain(emitted.Diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            foreach (string id in new[] { "CS1591", "CS1572", "CS1573" })
                Assert.Contains(emitted.Diagnostics, diagnostic => diagnostic.Id == id
                    && diagnostic.Location.SourceTree?.FilePath == "HandWritten.cs");
            Assert.DoesNotContain(emitted.Diagnostics, static diagnostic => diagnostic.Id == "CS1591"
                && diagnostic.Location.SourceTree?.FilePath != "HandWritten.cs");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
