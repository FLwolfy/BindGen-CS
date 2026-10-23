using System;
using System.IO;
using Xunit;
namespace BGCS.Tool.Tests;

public sealed class GeneratorOnlyCommandTests
{
    [Theory]
    [InlineData("build")]
    [InlineData("native-build")]
    [InlineData("supply-chain")]
    public void RemovedOrRepositoryCommandsCannotExecuteInTheShippedTool(string command)
    {
        using StringWriter error = new();
        Assert.Equal(2, CliInvocation.Run([command], Environment.CurrentDirectory, TextWriter.Null, error));
        Assert.Contains("Unknown command", error.ToString());
    }
    [Fact]
    public void WorkspaceCannotBuildNativeOrManagedProjects()
    {
        using StringWriter error = new();
        Assert.Equal(2, CliInvocation.Run(["workspace", "build", "missing-workspace.json"], Environment.CurrentDirectory, TextWriter.Null, error));
        Assert.Contains("validate|generate|diff", error.ToString());
    }
}
