using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using BGCS.CSharp.Emission;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class SparseDocumentationCompositionTests
{
    [Fact]
    public void SparseDocumentationSurvivesBodyHeavySubtreesWithoutChangingXmlProseOrRawLiteralContents()
    {
        StringBuilder middle = new("#nullable enable\nnamespace Generated.Sparse;\npublic static class Body {\n");
        for (int index = 0; index < 256; index++)
            middle.Append("private static int Step").Append(index)
                .Append("(int value) { /* Ordinary comments remain ordinary. */ if (value < 0) { return -value; } return value + ")
                .Append(index).AppendLine("; }");
        middle.AppendLine("/// <summary>Middle: 猫 &amp;&amp; α; A &lt; B &amp;&amp; B &gt; C.</summary>")
            .AppendLine("public static int Anchor(int value) => value;")
            .AppendLine("}");
        (string Path, string Text)[] sources =
        [
            ("c.cs", """"
                namespace Generated.Sparse;
                public static class Late {
                    /// <summary>Late: &amp;&amp; &lt;include file='absent.xml'/&gt; 𝄞.</summary>
                    public const string Value = """
                        /// A && B < C; 猫 > )
                        #pragma not a directive
                        """;
                    public const string Ordinary = "> )";
                    public static unsafe void Invoke(nint address) => ((delegate* unmanaged[Cdecl]<void>)address)();
                }
                """"),
            ("b.cs", middle.ToString()),
            ("a.cs", """
                #nullable enable
                namespace Generated.Sparse;
                /// <summary>Early: A &lt; B &amp;&amp; B &gt; C; 猫 𝄞.</summary>
                public sealed class Early { public object? Item { get; set; } }
                """)
        ];
        string composed = new SingleFileComposer().ComposeSources(sources, "Generated.Sparse");
        CSharpParseOptions parse = new(LanguageVersion.CSharp14, documentationMode: DocumentationMode.Diagnose);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(composed, parse);
        SyntaxNode root = tree.GetRoot();
        Assert.Equal(3, root.DescendantTrivia(descendIntoTrivia: false)
            .Count(trivia => trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)));
        string[] literals = root.DescendantNodes().OfType<LiteralExpressionSyntax>()
            .Where(expression => expression.Token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken))
            .Select(expression => expression.Token.ValueText.Replace("\r\n", "\n", StringComparison.Ordinal)).ToArray();
        Assert.Equal("/// A && B < C; 猫 > )\n#pragma not a directive", Assert.Single(literals));
        Assert.Single(root.DescendantNodes().OfType<CastExpressionSyntax>(), cast => cast.Type is FunctionPointerTypeSyntax);

        string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        CSharpCompilation compilation = CSharpCompilation.Create("SparseDocumentation", [tree],
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream assembly = new();
        using MemoryStream xml = new();
        var emitted = compilation.Emit(assembly, xmlDocumentationStream: xml);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        xml.Position = 0;
        XDocument document = XDocument.Load(xml);
        Assert.Empty(document.Descendants("include"));
        Dictionary<string, string> summaries = document.Descendants("member").ToDictionary(
            member => member.Attribute("name")!.Value, member => member.Element("summary")!.Value.Trim(), StringComparer.Ordinal);
        Assert.Equal(3, summaries.Count);
        Assert.Equal("Early: A < B && B > C; 猫 𝄞.", summaries["T:Generated.Sparse.Early"]);
        Assert.Equal("Middle: 猫 && α; A < B && B > C.", summaries["M:Generated.Sparse.Body.Anchor(System.Int32)"]);
        Assert.Equal("Late: && <include file='absent.xml'/> 𝄞.", summaries["F:Generated.Sparse.Late.Value"]);
        IFieldSymbol ordinary = (IFieldSymbol)Assert.Single(compilation.GetTypeByMetadataName("Generated.Sparse.Late")!.GetMembers("Ordinary"));
        Assert.Equal("> )", ordinary.ConstantValue);
        Assert.Equal(NullableAnnotation.Annotated,
            ((IPropertySymbol)Assert.Single(compilation.GetTypeByMetadataName("Generated.Sparse.Early")!.GetMembers("Item"))).Type.NullableAnnotation);
    }
}
