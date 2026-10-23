using System.IO;
using Xunit;

namespace BGCS.RepositoryTools.Tests;

public sealed class ArchitectureFriendTests
{
    [Theory]
    [InlineData("[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Example.Tests\")]")]
    [InlineData("using Friend = System.Runtime.CompilerServices.InternalsVisibleToAttribute; [assembly: Friend(\"Example.Tests\")]")]
    [InlineData("#if FRIEND_ENABLED\n[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Example.Tests\")]\n#endif")]
    [InlineData("[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(Names.Target)] internal static class Names { internal const string Target = \"Example.Tests\"; }")]
    public void ProductionFriendAccessIsRejectedWithoutTestExceptions(string declaration)
    {
        using ArchitectureFixture repository = new();
        repository.WriteSource("BGCS.Frontend.Clang", declaration);
        Assert.Equal(1, repository.Validate());
    }

    [Theory]
    [InlineData("<InternalsVisibleTo Include='Example.Tests'/>")]
    [InlineData("<AssemblyAttribute Include='System.Runtime.CompilerServices.InternalsVisibleToAttribute'><_Parameter1>Example.Tests</_Parameter1></AssemblyAttribute>")]
    public void ProjectGeneratedFriendAccessIsRejected(string declaration)
    {
        using ArchitectureFixture repository = new();
        repository.WriteProject("BGCS.Frontend.Clang", "<ItemGroup>" + declaration + "</ItemGroup>");
        Assert.Equal(1, repository.Validate());
    }

    [Fact]
    public void SharedBuildItemsCannotGrantFriendAccess()
    {
        using ArchitectureFixture repository = new();
        File.WriteAllText(Path.Combine(repository.root, "Directory.Build.props"), "<Project><ItemGroup><InternalsVisibleTo Include='Example.Tests'/></ItemGroup></Project>");
        Assert.Equal(1, repository.Validate());
    }

    [Fact]
    public void AttributeTextInsideAStringDoesNotGrantAccess()
    {
        using ArchitectureFixture repository = new();
        repository.WriteSource("BGCS.Frontend.Clang", """public class Literal { public const string Value = "[assembly: InternalsVisibleTo(\"Example.Tests\")]"; }""");
        Assert.Equal(0, repository.Validate());
    }
}
