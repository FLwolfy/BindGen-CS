using BGCS.Language;
using BGCS.Language.CSharp;
using BGCS.Language.CSharp.Nodes;
using Xunit;

namespace BGCS.Language.Tests;

public class CSharpParserTests
{
    [Fact]
    public void Parser_Namespace_ShouldMatchDebugTree()
    {
        string input = "namespace TestNamespace\r\n{\r\n}";
        CSharpParser parser = new(new());

        var result = parser.Parse(input, "test.cs");
        Assert.False(result.Diagnostics.HasErrors, result.Diagnostics.ToString());

        SyntaxTree expected = new(new RootNode(new()
        {
            new NamespaceNode("TestNamespace")
        }));

        Assert.Equal(expected.BuildDebugTree(), result.SyntaxTree?.BuildDebugTree());
    }

    [Fact]
    public void Parser_ClassAndMethod_ShouldMatchDebugTree()
    {
        string input = "namespace TestNamespace\r\n{\r\n    public class TestClass\r\n    {\r\n        public void TestMethod(string s)\r\n        {\r\n        }\r\n    }\r\n}";
        CSharpParser parser = new(new());

        var result = parser.Parse(input, "test.cs");
        Assert.False(result.Diagnostics.HasErrors, result.Diagnostics.ToString());

        SyntaxTree expected = new(new RootNode(new()
        {
            new NamespaceNode("TestNamespace", new()
            {
                new ClassNode("TestClass", new KeywordType[] { KeywordType.Public }, new()
                {
                    new MethodNode("TestMethod", new KeywordType[] { KeywordType.Public }, new[] { "string", "s" }, "void")
                })
            })
        }));

        Assert.Equal(expected.BuildDebugTree(), result.SyntaxTree?.BuildDebugTree());
    }

    [Fact]
    public void Parser_UsingNamespaceClassFieldMethod_ShouldMatchDebugTree()
    {
        string input = "using System;\nnamespace Demo {\npublic class C {\nint Value;\nvoid M(int x) { }\n}\n}";
        CSharpParser parser = new(new());

        var result = parser.Parse(input, "test.cs");
        Assert.False(result.Diagnostics.HasErrors, result.Diagnostics.ToString());

        SyntaxTree expected = new(new RootNode(new()
        {
            new UsingNode("System"),
            new NamespaceNode("Demo", new()
            {
                new ClassNode("C", new[] { KeywordType.Public }, new()
                {
                    new FieldNode("int", "Value", new KeywordType[] { }, null),
                    new MethodNode("M", new KeywordType[] { }, new[] { "int", "x" }, "void")
                })
            })
        }));

        Assert.Equal(expected.BuildDebugTree(), result.SyntaxTree?.BuildDebugTree());
    }

    [Fact]
    public void Parser_MissingClosingBrace_ShouldReportError()
    {
        string input = "namespace Demo { public class C {";
        CSharpParser parser = new(new());

        var result = parser.Parse(input, "broken.cs");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Null(result.SyntaxTree);
    }
}
