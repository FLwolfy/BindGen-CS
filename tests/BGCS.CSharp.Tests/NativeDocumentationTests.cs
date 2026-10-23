using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.CSharp.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class NativeDocumentationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeProse_RemainsInertValidXmlAcrossRawFriendlyStorageAndSharedApis(bool shared, bool singleFile)
    {
        const string hostile = "A < B && B > C, \"quoted\" and 'literal' 🐈.\r\n</summary>\n<include file='missing.xml' path='*'/>\n@param renamed native description\n#pragma warning disable\u0085public class Injected {}\u2028class Another {}\u2029\0\uD800";
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType record = new(new("record"), "Value", NativeTypeCategory.Record, 8, 4)
        {
            declarationName = "Value", documentation = "Record " + hostile,
            fields = [new(new("field"), "value", integer.id, 0, 0, null) { documentation = "Field " + hostile },
                new(new("bits"), "flags", integer.id, 4, 32, 3) { documentation = "Bitfield " + hostile }]
        };
        InteropType enumeration = new(new("enum"), "Kind", NativeTypeCategory.Enumeration, 4, 4)
        {
            declarationName = "Kind", elementType = integer.id, documentation = "Enum " + hostile,
            enumValues = [new(new("enumerator"), "First", "1", "1") { documentation = "Enumerator " + hostile }]
        };
        InteropFunction function = new(new("call"), "call", integer.id, [new(new("parameter"), "native_name", integer.id)], "C")
        { documentation = "Function " + hostile };
        InteropModule module = new("fixture", "windows-x64-msvc", 8, [integer, record, enumeration], [function], [])
        {
            byteOrder = NativeByteOrder.LittleEndian,
            constants = [new(new("constant"), "VALUE", "VALUE", new(NativeScalarConstantKind.Integer, "42", 4, true,
                new(EvidenceLevel.Declared, "fixture")), "Constant " + hostile)]
        };
        ManagedApiModel model = new DefaultManagedProjection().Project(module,
            new("Generated.Documentation", "Api") { style = shared ? ManagedApiStyle.Static : ManagedApiStyle.Instance }, CancellationToken.None);
        MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink) { singleFile = singleFile }, CancellationToken.None);
        XDocument document = CompileXml(sink);
        Assert.Empty(document.Descendants("include"));
        Assert.Empty(document.Descendants("param"));
        string[] summaries = document.Descendants("summary").Select(element => element.Value).ToArray();
        foreach (string prefix in new[] { "Record ", "Field ", "Bitfield ", "Enum ", "Enumerator ", "Constant " })
            Assert.Contains(summaries, summary => summary.Contains(prefix + "A < B && B > C", StringComparison.Ordinal));
        Assert.Equal(shared ? 3 : 2, summaries.Count(summary => summary.Contains("Function A < B", StringComparison.Ordinal)));
        Assert.All(summaries.Where(summary => summary.Contains("A < B", StringComparison.Ordinal)), summary =>
        {
            Assert.Contains("🐈", summary, StringComparison.Ordinal);
            Assert.Contains("<include file='missing.xml' path='*'/>", summary, StringComparison.Ordinal);
            Assert.Contains("@param renamed native description", summary, StringComparison.Ordinal);
            Assert.Contains("\u0085public class Injected {}\u2028class Another {}\u2029\uFFFD\uFFFD", summary, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParsedSectionsFollowManagedParameterRenamesAndRetainUnmatchedNativeDescriptions(bool shared)
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropFunction function = new(new("call"), "call", integer.id, [new(new("parameter"), "native_input", integer.id)], "C")
        {
            documentationContent = new("Computes A && B.", [new("native_input", 0, "A < B && C."), new("unknown_name", null, "Unresolved native parameter.")], "The native result.")
            { typeParameters = [new("T", "Concrete native template argument.")] }
        };
        ManagedApiModel model = new DefaultManagedProjection().Project(new("fixture", "windows-x64-msvc", 8, [integer], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian },
            new("Generated.Documentation", "Api") { style = shared ? ManagedApiStyle.Static : ManagedApiStyle.Instance }, CancellationToken.None);
        model = model with { methods = [model.methods[0] with { parameterNames = ["renamed"] }] };
        MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        XDocument document = CompileXml(sink);
        XElement[] parameters = document.Descendants("param").ToArray();
        Assert.Equal(shared ? 3 : 2, parameters.Length);
        Assert.All(parameters, parameter => { Assert.Equal("renamed", parameter.Attribute("name")!.Value); Assert.Contains("A < B && C.", parameter.Value, StringComparison.Ordinal); });
        Assert.Empty(document.Descendants("typeparam"));
        Assert.All(document.Descendants("returns"), result => Assert.Contains("The native result.", result.Value, StringComparison.Ordinal));
        Assert.Equal(shared ? 3 : 2, document.Descendants("returns").Count());
        Assert.Equal(shared ? 3 : 2, document.Descendants("remarks").Count());
        Assert.All(document.Descendants("remarks"), remarks =>
        {
            Assert.Contains("Native parameter unknown_name: Unresolved native parameter.", remarks.Value, StringComparison.Ordinal);
            Assert.Contains("Native template parameter T: Concrete native template argument.", remarks.Value, StringComparison.Ordinal);
        });
    }

    private static XDocument CompileXml(MemorySink sink)
    {
        CSharpParseOptions parse = new(LanguageVersion.CSharp14, documentationMode: DocumentationMode.Diagnose);
        SyntaxTree[] trees = sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, parse, source.Key)).ToArray();
        Assert.All(trees, tree => Assert.Contains("#pragma warning disable CS1591", tree.GetText().ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(trees.SelectMany(tree => tree.GetRoot().DescendantNodes()),
            node => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax declaration && declaration.Identifier.ValueText is "Injected" or "Another");
        string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        CSharpCompilation compilation = CSharpCompilation.Create("NativeDocumentation", trees,
            trusted.Split(Path.PathSeparator).Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream assembly = new();
        using MemoryStream xml = new();
        var result = compilation.Emit(assembly, xmlDocumentationStream: xml);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        xml.Position = 0;
        return XDocument.Load(xml);
    }

    private sealed class MemorySink : IArtifactSink
    {
        internal Dictionary<string, string> sources { get; } = new(StringComparer.Ordinal);
        public string WriteText(string relativePath, string content)
        {
            sources.Add(relativePath, content);
            return relativePath;
        }
    }
}
