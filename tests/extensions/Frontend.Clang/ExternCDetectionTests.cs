using System.Linq;
using System.Threading.Tasks;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ExternCDetectionTests : FrontendTestHost
{
    [Theory]
    [InlineData("extern \"C\" int Bgcs_Extern(int a);", "Bgcs_Extern", "C")]
    [InlineData("int Bgcs_Normal(int a);", "Bgcs_Normal", "C++")]
    [InlineData("namespace Demo { extern \"C\" { int Bgcs_ExternNs(int a); } }", "Bgcs_ExternNs", "C")]
    public async Task LanguageLinkageRemainsIndependentOfItsDeclarationContainer(
        string source,
        string name,
        string linkage
    ) {
        NativeModule module = (await ParseSourceAsync(source)).module;
        Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.Equal(linkage, Assert.Single(module.declarations, value => value.kind == "Function" && value.name == name).linkage);
    }
    [Fact]
    public async Task ClassMemberInsideExternCBlockRemainsAMember()
    {
        NativeModule module = (await ParseSourceAsync("extern \"C\" { class DemoClass { public: int Bgcs_Method(int a); }; }")).module;
        NativeDeclaration member = Assert.Single(module.declarations, value => value.name == "Bgcs_Method");
        Assert.NotNull(member.parent);
        Assert.Equal("Class", module.GetDeclaration(member.parent!.Value).kind);
        Assert.NotEqual("C", member.linkage);
    }
    [Fact]
    public async Task MixedLinkageDoesNotLeakIntoFollowingDeclarations()
    {
        NativeModule module = (await ParseSourceAsync("int Bgcs_Normal(int a); extern \"C\" { int Bgcs_ExternOnly(int a); } namespace Demo { int Bgcs_NamespaceOnly(int a); }")).module;
        Assert.Equal("C", module.declarations.Single(value => value.name == "Bgcs_ExternOnly").linkage);
        Assert.All(module.declarations.Where(value => value.name is "Bgcs_Normal" or "Bgcs_NamespaceOnly"), value => Assert.NotEqual("C", value.linkage));
    }
}
