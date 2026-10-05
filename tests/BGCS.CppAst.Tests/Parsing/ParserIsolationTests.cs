using System.Linq;
using System.Threading.Tasks;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;
using Xunit;

namespace BGCS.CppAst.Tests.Parsing;

public sealed class ParserIsolationTests
{
    [Fact]
    public void UnicodeDeclarationNames_PreserveDistinctContainersAndResolvedReferences()
    {
        using CppCompilation compilation = CppParser.Parse("""
            namespace 世界甲 { struct 数据 { int first; }; }
            namespace 世界乙 { struct 数据 { double second; }; }
            世界甲::数据 left;
            世界乙::数据 right;
            """);

        Assert.False(compilation.hasErrors, compilation.diagnostics.ToString());
        Assert.Equal(2, compilation.namespaces.Count);
        CppNamespace first = compilation.namespaces.Single(value => value.name == "世界甲");
        CppNamespace second = compilation.namespaces.Single(value => value.name == "世界乙");
        Assert.Equal("first", Assert.Single(Assert.Single(first.classes).fields).name);
        Assert.Equal("second", Assert.Single(Assert.Single(second.classes).fields).name);
        Assert.Same(first.classes[0], compilation.fields.Single(value => value.name == "left").type.GetCanonicalType());
        Assert.Same(second.classes[0], compilation.fields.Single(value => value.name == "right").type.GetCanonicalType());
    }

    [Fact]
    public async Task ConcurrentCompilations_KeepVisitorsAndDeclarationsWithinTheirOwner()
    {
        Task[] parses = Enumerable.Range(0, 12).Select(index => Task.Run(() => {
            string namespaceName = "Owner" + index;
            string source = $"namespace {namespaceName} {{ struct Value {{ int field; }}; Value make(Value input); }}";
            for (int attempt = 0; attempt < 8; attempt++)
            {
                using CppCompilation compilation = CppParser.Parse(source);
                Assert.False(compilation.hasErrors, compilation.diagnostics.ToString());
                CppNamespace owner = Assert.Single(compilation.namespaces);
                Assert.Equal(namespaceName, owner.name);
                CppClass value = Assert.Single(owner.classes);
                CppFunction function = Assert.Single(owner.functions);
                Assert.Same(value, function.returnType.GetCanonicalType());
                Assert.Same(value, Assert.Single(function.parameters).type.GetCanonicalType());
            }
        })).ToArray();

        await Task.WhenAll(parses);
    }
}
