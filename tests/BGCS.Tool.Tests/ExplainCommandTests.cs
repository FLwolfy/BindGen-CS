using System;
using System.IO;
using BGCS.Tool.Commands;
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
        Assert.Contains("LengthParameter", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Json_ProducesMachineReadableDescriptor()
    {
        CommandResult result = Run("BGCSCPP001", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"Code\": \"BGCSCPP001\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"Resolution\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_UnknownCode_ReturnsUsageError()
    {
        CommandResult result = Run("BGCS-NOT-REAL");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unknown diagnostic", result.Error, StringComparison.Ordinal);
    }

    private static CommandResult Run(params string[] args)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        int exitCode = ExplainCommand.Run(args, output, error);
        return new(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}
