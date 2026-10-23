using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BGCS.Core.Artifacts;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class SingleFileEmissionParityTests
{
    private const string Prose = "A < B && B > C; 猫 𝄞.\nAn inert <include file='absent.xml'/> remains prose.";
    private const string Literal = "\"\"\"\n    猫 && α\n    A < B > C\n    \"\"\"";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DirectSingleFileMatchesComposedSplitBytesAndPreservesDocumentationAndRawLiteralValues(bool runtimeSource, bool metadata)
    {
        ManagedApiModel source = NeutralBackendTests.ModelWithStorage();
        InteropModule module = new(source.interop.libraryId, source.interop.targetId, source.interop.pointerSize,
            source.interop.types.Select(type => type with
            {
                documentation = Prose,
                fields = type.fields.Select(field => field with { documentation = Prose }).ToImmutableArray(),
                enumValues = type.enumValues.Select(value => value with { documentation = Prose }).ToImmutableArray()
            }).ToImmutableArray(),
            source.interop.functions.Select(function => function with { documentation = Prose }).ToImmutableArray(), [])
        { byteOrder = source.interop.byteOrder };
        ManagedApiModel model = new DefaultManagedProjection().Project(module,
            new(source.@namespace, source.name), CancellationToken.None);
        model = model with
        {
            emitNativeMetadata = metadata,
            methods = model.methods.Select(method => method.nativeExport.value == "add"
                ? method with { returnProjection = new("string", null, Literal) }
                : method).ToImmutableArray()
        };
        NeutralBackendTests.MemorySink split = new();
        NeutralBackendTests.MemorySink single = new();
        EmissionRequest request = new("fixture", split)
        {
            includeRuntimeSource = runtimeSource,
            runtimeNamespace = runtimeSource ? "Generated.Example.Runtime" : "BGCS.Runtime"
        };
        CSharpBackend backend = new();
        await backend.EmitAsync(model, PlanningFixture.Prepare(model, request), CancellationToken.None);
        ImmutableArray<ArtifactDescriptor> artifacts = await backend.EmitAsync(model,
            PlanningFixture.Prepare(model, request with { sink = single, singleFile = true }), CancellationToken.None);

        string expected = new SingleFileComposer().ComposeSources(split.sources.Select(pair => (pair.Key, pair.Value)),
            model.@namespace, module.targetId);
        string actual = Assert.Single(single.sources).Value;
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expected))), Assert.Single(artifacts).digest.value);
        Assert.Equal("Bindings.cs", Assert.Single(artifacts).relativePath);
        Assert.Contains("&amp;&amp;", actual, StringComparison.Ordinal);

        string[] splitLiterals = RawLiteralValues(split.sources);
        Assert.NotEmpty(splitLiterals);
        Assert.All(splitLiterals, value => Assert.Equal("猫 && α\nA < B > C", value.Replace("\r\n", "\n", StringComparison.Ordinal)));
        Assert.Equal(splitLiterals, RawLiteralValues(single.sources));

        XDocument splitXml = CompileXml(split.sources);
        XDocument singleXml = CompileXml(single.sources);
        Assert.Equal(DocumentationMembers(splitXml), DocumentationMembers(singleXml));
        Assert.Empty(singleXml.Descendants("include"));
        Assert.Contains(singleXml.Descendants("summary"), summary => summary.Value.Contains("A < B && B > C; 猫 𝄞.", StringComparison.Ordinal));
    }

    [Fact]
    public void ComposerChecksEveryFragmentAndHonorsCancellationBeforeReturningAResult()
    {
        SingleFileComposer composer = new();
        InvalidOperationException invalid = Assert.Throws<InvalidOperationException>(() => composer.ComposeSources(
            [("a.cs", "namespace Generated; public class Value {}"), ("b.cs", "namespace Generated; public class Broken {")], "Generated"));
        Assert.Contains("b.cs", invalid.Message, StringComparison.Ordinal);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        OperationCanceledException cancellation = Assert.Throws<OperationCanceledException>(() => composer.ComposeSources(
            [("b.cs", "namespace Generated; public class Broken {")], "Generated", cancellationToken: cancelled.Token));
        Assert.Equal(cancelled.Token, cancellation.CancellationToken);
        Assert.Contains("public class Value", composer.ComposeSources([("a.cs", "namespace Generated; public class Value {}")], "Generated"), StringComparison.Ordinal);
    }

    private static string[] RawLiteralValues(IReadOnlyDictionary<string, string> sources) => sources.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .SelectMany(pair => CSharpSyntaxTree.ParseText(pair.Value, new(LanguageVersion.CSharp14), pair.Key).GetRoot().DescendantNodes()
            .OfType<LiteralExpressionSyntax>().Where(literal => literal.Token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken))
            .Select(literal => literal.Token.ValueText)).Order(StringComparer.Ordinal).ToArray();

    private static string[] DocumentationMembers(XDocument document) => document.Descendants("member")
        .Select(member => member.Attribute("name")!.Value + "\n" + string.Join("\n", member.Elements().Select(element => element.Name + ":"
            + string.Join("\n", element.Value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.Trim())))))
        .Order(StringComparer.Ordinal).ToArray();

    private static XDocument CompileXml(IReadOnlyDictionary<string, string> sources)
    {
        CSharpParseOptions parse = new(LanguageVersion.CSharp14, documentationMode: DocumentationMode.Diagnose);
        string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        CSharpCompilation compilation = CSharpCompilation.Create("SingleFileParity",
            sources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, parse, pair.Key)),
            trusted.Split(Path.PathSeparator).Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream assembly = new();
        using MemoryStream xml = new();
        var emitted = compilation.Emit(assembly, xmlDocumentationStream: xml);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        xml.Position = 0;
        return XDocument.Load(xml);
    }
}
