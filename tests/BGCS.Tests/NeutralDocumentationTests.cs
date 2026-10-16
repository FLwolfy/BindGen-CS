using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class NeutralDocumentationTests
{
    [Theory]
    [InlineData(SourceLanguage.C, false)]
    [InlineData(SourceLanguage.C, true)]
    [InlineData(SourceLanguage.Cpp, false)]
    [InlineData(SourceLanguage.Cpp, true)]
    public async Task WorkerDocumentationSectionsSurviveNeutralAnalysisAndBridgeReparse(SourceLanguage language, bool singleFile)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-documentation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "docs.h"), """
                /** Computes a value.
                 * @param input Input value less than 10.
                 * @return The computed result.
                 */
                int compute(int input);
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "documentation", nativeName = "unused" },
                source = new() { language = language, headers = ["docs.h"], parseMacros = false },
                api = new() { @namespace = "Generated.Documentation", name = "Api", names = ImmutableDictionary<string, string>.Empty.Add("compute::input", "renamed") },
                output = new() { layout = singleFile ? "singleFile" : "split" },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult result = await host.GenerateAsync(new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            var native = Assert.Single(target.native.declarations, declaration => declaration.name == "compute");
            Assert.NotNull(native.documentationContent);
            Assert.Equal("input", Assert.Single(native.documentationContent.parameters).name);
            Assert.Equal(0, native.documentationContent.parameters[0].index);
            Assert.Equal("The computed result.", native.documentationContent.returns);
            var function = Assert.Single(target.interop.functions, function => function.sourceName == "compute");
            Assert.Equal(native.documentationContent, function.documentationContent);
            CSharpParseOptions parse = new(LanguageVersion.CSharp14, documentationMode: DocumentationMode.Diagnose);
            SyntaxTree[] trees = result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
                .Select(artifact => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(result.outputRoot, artifact.relativePath)), parse, artifact.relativePath)).ToArray();
            string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
            CSharpCompilation compilation = CSharpCompilation.Create("NeutralDocumentation", trees,
                trusted.Split(Path.PathSeparator).Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
                new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, generalDiagnosticOption: ReportDiagnostic.Error));
            using MemoryStream assembly = new();
            using MemoryStream xml = new();
            var compiled = compilation.Emit(assembly, xmlDocumentationStream: xml);
            Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics));
            xml.Position = 0;
            XDocument documentation = XDocument.Load(xml);
            XElement[] methods = documentation.Descendants("member").Where(member => member.Element("summary")?.Value.Contains("Computes a value.", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(2, methods.Length);
            Assert.All(methods, method =>
            {
                Assert.Contains(method.Elements("param"), parameter => parameter.Attribute("name")!.Value == "renamed"
                    && parameter.Value.Contains("Input value less than 10.", StringComparison.Ordinal));
            });
            Assert.Contains(methods, method => method.Element("returns")?.Value.Contains("The computed result.", StringComparison.Ordinal) == true);
            if (language == SourceLanguage.Cpp)
            {
                XElement raw = Assert.Single(methods, method => method.Attribute("name")!.Value.Contains(".RawApi.", StringComparison.Ordinal));
                Assert.Contains("native status code", raw.Element("returns")!.Value, StringComparison.Ordinal);
                Assert.Contains("Native result: The computed result.", raw.Element("remarks")!.Value, StringComparison.Ordinal);
                Assert.Equal(function.parameters.Length, raw.Elements("param").Count());
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
