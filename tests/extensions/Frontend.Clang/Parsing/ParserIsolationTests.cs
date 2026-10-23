using System.Linq;
using System.Threading.Tasks;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ParserIsolationTests : FrontendTestHost
{
    [Fact]
    public async Task UnicodeDeclarationNamesPreserveDistinctContainersAndResolvedReferences()
    {
        NativeModule module = (await ParseSourceAsync("namespace 世界甲 { struct 数据 { int first; }; } namespace 世界乙 { struct 数据 { double second; }; } 世界甲::数据 left; 世界乙::数据 right;")).module;
        Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
        NativeDeclaration first = Assert.Single(module.declarations, declaration => declaration.qualifiedName == "世界甲::数据");
        NativeDeclaration second = Assert.Single(module.declarations, declaration => declaration.qualifiedName == "世界乙::数据");
        Assert.NotEqual(first.id, second.id);
        Assert.Equal(first.type, module.declarations.Single(declaration => declaration.name == "left").type);
        Assert.Equal(second.type, module.declarations.Single(declaration => declaration.name == "right").type);
        Assert.Equal("first", module.GetDeclaration(Assert.Single(first.children)).name);
        Assert.Equal("second", module.GetDeclaration(Assert.Single(second.children)).name);
    }
    [Fact]
    public async Task ConcurrentCompilationsKeepDeclarationsAndReferencesWithinTheirOwner()
    {
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async index => {
            string owner = "Owner" + index;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                NativeModule module = (await ParseSourceAsync($"namespace {owner} {{ struct Value {{ int field; }}; Value make(Value input); }}")).module;
                Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
                NativeDeclaration record = Assert.Single(module.declarations, value => value.qualifiedName == owner + "::Value");
                NativeDeclaration function = Assert.Single(module.declarations, value => value.name == "make");
                Assert.Equal(record.type, function.returnType);
                Assert.All(module.declarations.Where(value => value.kind == "Parameter"), value => Assert.Equal(record.type, value.type));
                Assert.DoesNotContain(module.declarations, value => value.qualifiedName.StartsWith("Owner") && !value.qualifiedName.StartsWith(owner));
            }
        }));
    }
}
