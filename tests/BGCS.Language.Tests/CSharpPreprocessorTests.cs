using System;
using BGCS.Language.CSharp.Preprocessing;
using Xunit;

namespace BGCS.Language.Tests;

public sealed class CSharpPreprocessorTests
{
    [Fact]
    public void ConditionalBranchesPreserveActiveSourceOffsetsAndLineEndings()
    {
        string input = "#if FOO && !BAR\r\nclass Active { }\r\n#elif BAR\r\nclass Other { }\r\n#else\r\nclass Inactive { }\r\n#endif";
        string result = new CSharpPreprocessor(["FOO"]).Process(input, "fixture.cs");

        Assert.Equal(input.Length, result.Length);
        Assert.Equal(input.IndexOf("class Active", StringComparison.Ordinal), result.IndexOf("class Active", StringComparison.Ordinal));
        Assert.DoesNotContain("Other", result);
        Assert.DoesNotContain("Inactive", result);
        for (int index = 0; index < input.Length; index++)
            if (input[index] is '\r' or '\n')
                Assert.Equal(input[index], result[index]);
    }

    [Fact]
    public void SourceDefinedSymbolsAndInactiveErrorsDoNotEscapeAnInvocation()
    {
        var preprocessor = new CSharpPreprocessor();
        string first = preprocessor.Process("#define FOO\n#if FOO\nclass Active { }\n#else\n#error excluded\n#endif", "first.cs");
        string second = preprocessor.Process("#if FOO\n#error leaked\n#else\nclass Second { }\n#endif", "second.cs");

        Assert.Contains("class Active", first);
        Assert.Contains("class Second", second);
    }

    [Theory]
    [InlineData("#if FOO")]
    [InlineData("#else\nclass Value { }")]
    [InlineData("#error expected failure")]
    [InlineData("#if (FOO &&)\n#endif")]
    public void InvalidDirectivesFailWithTheirSourceName(string text)
    {
        FormatException failure = Assert.Throws<FormatException>(() => new CSharpPreprocessor().Process(text, "invalid.cs"));
        Assert.Contains("invalid.cs", failure.Message);
    }

    [Fact]
    public void DirectiveFreeSourceIsReturnedWithoutCopyingAndSymbolNamesAreValidated()
    {
        string input = "class Value { string text = \"#if FOO\"; }";
        Assert.Same(input, new CSharpPreprocessor().Process(input, "value.cs"));
        Assert.Throws<ArgumentException>(() => new CSharpPreprocessor(["@FOO"]));
        Assert.Throws<ArgumentException>(() => new CSharpPreprocessor(["with space"]));
    }
}
