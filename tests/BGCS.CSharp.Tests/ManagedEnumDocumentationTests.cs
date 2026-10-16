using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ManagedEnumDocumentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlainDocumentationSurvivesNamespaceAndNestedSourceLayouts(bool nested)
    {
        const string prose = "Plain enum comment & <value>.";
        const string itemProse = "Plain item comment: \"One\".";
        ManagedEnumDefinition definition = new("CustomMode", ManagedIntegralKind.UInt64,
            [new("One", "18446744073709551615") { documentation = itemProse }])
        { documentation = prose, isFlags = true };
        ManagedApiModel original = NeutralBackendTests.Model();
        ManagedApiModel model = original with
        {
            enums = [definition], nestGeneratedTypesInApi = nested,
            style = nested ? ManagedApiStyle.Static : ManagedApiStyle.Instance
        };
        (CSharpCompilation compilation, CSharpEmissionPlan plan) = await CompileAsync(model, nested);
        INamedTypeSymbol declaration = Enum(compilation, nested);
        Assert.Equal(prose, Summary(declaration));
        IFieldSymbol value = Assert.Single(declaration.GetMembers("One").OfType<IFieldSymbol>());
        Assert.Equal(itemProse, Summary(value));
        Assert.Equal(ulong.MaxValue, value.ConstantValue);
        Assert.Equal(SpecialType.System_UInt64, declaration.EnumUnderlyingType!.SpecialType);
        Assert.Contains(declaration.GetAttributes(), attribute => attribute.AttributeClass!.ToDisplayString() == "System.FlagsAttribute");
        CSharpManagedEnumPlan planned = Assert.Single(plan.managedEnums);
        Assert.Equal(prose, planned.documentation);
        Assert.Equal(itemProse, Assert.Single(planned.values).documentation);
        Assert.Same(original.interop, model.interop);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OmittedDocumentationFollowsOnlyTheExplicitPlaceholderChoice(bool placeholders)
    {
        ManagedEnumDefinition definition = new("CustomMode", ManagedIntegralKind.Int32, [new("One", "1")]);
        ManagedApiModel model = NeutralBackendTests.Model() with { enums = [definition], emitPlaceholderComments = placeholders };
        (CSharpCompilation compilation, _) = await CompileAsync(model, false);
        INamedTypeSymbol declaration = Enum(compilation, false);
        string expected = placeholders ? "To be documented." : string.Empty;
        Assert.Equal(expected, Summary(declaration));
        Assert.Equal(expected, Summary(Assert.Single(declaration.GetMembers("One").OfType<IFieldSymbol>())));
        Assert.Null(definition.documentation);
        Assert.Null(Assert.Single(definition.values).documentation);
    }

    [Fact]
    public async Task SourceMarkersAndXmlIncludesRemainInertDocumentationText()
    {
        const string prose = "Preformatted enum comment\n*/\n#error injected\npublic class Injected {}\n<include file=\"missing.xml\" path=\"/*\" />";
        const string itemProse = "// Preformatted item comment && <summary>data</summary>";
        ManagedApiModel model = NeutralBackendTests.Model() with
        {
            enums = [new("CustomMode", ManagedIntegralKind.Int32,
                [new("One", "1") { documentation = itemProse }]) { documentation = prose }]
        };
        (CSharpCompilation compilation, _) = await CompileAsync(model, false);
        INamedTypeSymbol declaration = Enum(compilation, false);
        // Roslyn pretty-prints multiline XML with indentation; compare every original text line.
        Assert.Equal(prose.Split('\n'), Summary(declaration).Split('\n').Select(line => line.TrimStart()));
        Assert.Equal(itemProse, Summary(Assert.Single(declaration.GetMembers("One").OfType<IFieldSymbol>())));
        Assert.Null(compilation.GetTypeByMetadataName("Generated.Example.Injected"));
        Assert.DoesNotContain(compilation.SyntaxTrees.SelectMany(tree => tree.GetRoot().DescendantTrivia(descendIntoTrivia: true)),
            trivia => trivia.IsKind(SyntaxKind.ErrorDirectiveTrivia));
        XElement xml = XElement.Parse(declaration.GetDocumentationCommentXml(expandIncludes: true)!);
        Assert.Empty(xml.Descendants("include"));
        Assert.Single(compilation.SyntaxTrees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>()),
            enumeration => enumeration.Identifier.ValueText == "CustomMode");
    }

    [Fact]
    public void OptionalDocumentationRoundTripsWithoutChangingExplicitTypedValues()
    {
        JsonSerializerOptions options = new() { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        ManagedEnumDefinition definition = JsonSerializer.Deserialize<ManagedEnumDefinition>(
            """{"name":"CustomMode","underlyingType":"int32","values":[{"name":"One","value":"1"}]}""", options)!;
        Assert.Equal(ManagedIntegralKind.Int32, definition.underlyingType);
        Assert.Null(definition.documentation);
        Assert.Null(Assert.Single(definition.values).documentation);
        ManagedEnumDefinition documented = definition with
        {
            documentation = "Plain enum comment",
            values = [definition.values[0] with { documentation = "Plain item comment" }]
        };
        ManagedEnumDefinition roundTrip = JsonSerializer.Deserialize<ManagedEnumDefinition>(JsonSerializer.Serialize(documented, options), options)!;
        Assert.Equal("Plain enum comment", roundTrip.documentation);
        Assert.Equal("Plain item comment", Assert.Single(roundTrip.values).documentation);
        Assert.Equal("1", roundTrip.values[0].value);
        Assert.Equal(definition.underlyingType, roundTrip.underlyingType);
        Assert.Null(definition.documentation);
    }

    private static INamedTypeSymbol Enum(CSharpCompilation compilation, bool nested)
        => compilation.GetTypeByMetadataName(nested ? "Generated.Example.NativeApi+CustomMode" : "Generated.Example.CustomMode")
            ?? throw new InvalidOperationException("The managed enum was not emitted in its declared scope.");

    private static string Summary(ISymbol symbol)
    {
        string? xml = symbol.GetDocumentationCommentXml(expandIncludes: true);
        return string.IsNullOrWhiteSpace(xml) ? string.Empty : XElement.Parse(xml).Element("summary")?.Value.Trim() ?? string.Empty;
    }

    private static async Task<(CSharpCompilation Compilation, CSharpEmissionPlan Plan)> CompileAsync(ManagedApiModel model, bool sourceRuntime)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { singleFile = sourceRuntime, includeRuntimeSource = sourceRuntime,
            runtimeNamespace = sourceRuntime ? "Generated.Example.Runtime" : "BGCS.Runtime" };
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("ManagedEnumDocs_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value,
                new(LanguageVersion.CSharp14, documentationMode: DocumentationMode.Diagnose), source.Key)),
            references.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var result = compilation.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return (compilation, plan);
    }
}
