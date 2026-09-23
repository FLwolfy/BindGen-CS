using System;
using System.IO;
using BGCS.Intermediate;
using BGCS.Tool.Commands;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class GenerationDiagnosticWriterTests
{
    [Fact]
    public void WriteFailure_StructuredDiagnostics_PrintsStableCodeAndGuidanceOnce()
    {
        BindingDiagnostic diagnostic = new(
            BindingDiagnosticSeverity.Error,
            "The declaration cannot be emitted losslessly.",
            BindingDiagnosticCodes.CSharpUnsupported);
        BindingDiagnostic warning = new(
            BindingDiagnosticSeverity.Warning,
            "A lower-priority warning.",
            "BGCS-SAFETY-LENGTH");
        BindingGenerationResult result = new(null, false, [], [warning, diagnostic, diagnostic]);
        using StringWriter error = new();

        GenerationDiagnosticWriter.WriteFailure(result, error);

        string text = error.ToString();
        Assert.Contains("error: BGCSCS001: The declaration cannot be emitted losslessly.", text);
        Assert.Contains("bindgen-cs explain BGCSCS001", text);
        Assert.Equal(1, Count(text, "The declaration cannot be emitted losslessly."));
        Assert.Equal(1, Count(text, "bindgen-cs explain BGCSCS001"));
        Assert.DoesNotContain("A lower-priority warning", text);
    }

    [Fact]
    public void WriteFailure_MissingResult_PrintsFallbackMessage()
    {
        using StringWriter error = new();

        GenerationDiagnosticWriter.WriteFailure(null, error);

        Assert.Contains("failed without a structured diagnostic", error.ToString());
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }
}
