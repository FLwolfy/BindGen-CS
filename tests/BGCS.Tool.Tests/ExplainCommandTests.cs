using System;
using System.IO;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class ExplainCommandTests
{
    [Fact]
    public void Run_WithoutCode_ListsStableCatalog()
    {
        CommandResult result = Run();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("BGCS-SAFETY-OWNERSHIP", result.Output, StringComparison.Ordinal);
        Assert.Contains("BGCSCS001", result.Output, StringComparison.Ordinal);
        Assert.Contains("BGCSCPP001", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_KnownCode_PrintsCauseAndResolution()
    {
        CommandResult result = Run("bgcs-safety-length");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Cause:", result.Output, StringComparison.Ordinal);
        Assert.Contains("lengthParameter", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Json_ProducesMachineReadableDescriptor()
    {
        CommandResult result = Run("BGCSCPP001", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"code\": \"BGCSCPP001\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"resolution\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_UnknownCode_ReturnsUsageError()
    {
        CommandResult result = Run("BGCS-NOT-REAL");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unknown diagnostic", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_OutputFailure_DescribesPublicationAndPreservedOutput()
    {
        CommandResult result = Run("BGCSIO001");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("file-system", result.Output, StringComparison.Ordinal);
        Assert.Contains("preserves prior output", result.Output, StringComparison.Ordinal);
    }

    private static CommandResult Run(params string[] args)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        int exitCode = CliInvocation.Run(["explain", .. args], Environment.CurrentDirectory, output, error);
        return new(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}
