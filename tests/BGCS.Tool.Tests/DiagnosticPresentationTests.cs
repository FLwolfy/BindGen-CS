using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class DiagnosticPresentationTests
{
    [Fact]
    public void FrontendThreshold_OnlyFiltersConsoleAndPreservesStructuredWarnings()
    {
        WithInput("#warning BGCS_NOTICE\nint native_add(int left, int right);", directory =>
        {
            using StringWriter output = new();
            using StringWriter error = new();
            Assert.Equal(0, CliInvocation.Run(["generate", "bindgen.json", "--json", "--log-level", "none",
                "--frontend-log-level", "error"], directory, output, error));
            Assert.DoesNotContain("BGCS_NOTICE", error.ToString(), StringComparison.Ordinal);
            using JsonDocument hidden = JsonDocument.Parse(output.ToString());
            Assert.Contains(hidden.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("message").GetString()!.Contains("BGCS_NOTICE", StringComparison.Ordinal));
            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            Assert.Equal(0, CliInvocation.Run(["inspect", "bindgen.json", "--json", "--log-level", "none",
                "--frontend-log-level", "warning"], directory, output, error));
            Assert.Contains("BGCS_NOTICE", error.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NoneThreshold_CannotHideErrorsOrTurnFailureIntoSuccess()
    {
        WithInput("int broken( ;", directory =>
        {
            using StringWriter output = new();
            using StringWriter error = new();
            Assert.Equal(1, CliInvocation.Run(["generate", "bindgen.json", "--json", "--log-level", "none",
                "--frontend-log-level", "none"], directory, output, error));
            Assert.Contains("error:", error.ToString(), StringComparison.Ordinal);
            using JsonDocument result = JsonDocument.Parse(output.ToString());
            Assert.False(result.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(result.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("severity").GetString() is "error" or "critical");
            Assert.False(Directory.Exists(Path.Combine(directory, "Generated")));
        });
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("1")]
    public void UnsupportedThreshold_IsAnArgumentError(string severity)
    {
        using StringWriter error = new();
        Assert.Equal(2, CliInvocation.Run(["generate", "--log-level", severity], Environment.CurrentDirectory, TextWriter.Null, error));
        Assert.Contains("Unknown diagnostic severity", error.ToString(), StringComparison.Ordinal);
    }

    private static void WithInput(string source, Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-reporting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "native.h"), source);
            File.WriteAllText(Path.Combine(directory, "bindgen.json"), """
                { "schemaVersion": 2, "library": { "id": "native", "nativeName": "native" },
                  "source": { "headers": ["native.h"] }, "api": { "namespace": "Reporting.Native", "name": "NativeApi" } }
                """);
            action(directory);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
