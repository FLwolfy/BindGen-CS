using BGCS.Language;
using Xunit;

namespace BGCS.Language.Tests;

public class PreprocessorTests
{
    [Fact]
    public void Preprocessor_IfBlock_ShouldProcessWithoutErrors()
    {
        string input = "namespace TestNamespace\r\n{\r\n#if Test\r\npublic class TestClass { }\r\n#endif\r\n}";
        Preprocessor preprocessor = new();

        var result = preprocessor.Process(input, "");
        Assert.NotNull(result);
    }

    [Fact]
    public void Preprocessor_IsNewLineOrEof_NewLine_ShouldReturnTrueWithLength()
    {
        string input = "a\nb";
        bool ok = Preprocessor.IsNewLineOrEof(input, 1, out int len);

        Assert.True(ok);
        Assert.Equal(1, len);
    }

    [Fact]
    public void Preprocessor_IsNewLineOrEof_CrLf_ShouldReturnTrueWithLength2()
    {
        string input = "a\r\nb";
        bool ok = Preprocessor.IsNewLineOrEof(input, 1, out int len);

        Assert.True(ok);
        Assert.Equal(2, len);
    }

    [Fact]
    public void Preprocessor_IsNewLineOrEof_EofIndex_ShouldReturnTrueAndLengthZero()
    {
        string input = "abc";
        bool ok = Preprocessor.IsNewLineOrEof(input, input.Length, out int len);

        Assert.True(ok);
        Assert.Equal(0, len);
    }

    [Fact]
    public void Preprocessor_Process_NoTrailingNewLine_ShouldReturnImmediately()
    {
        string input = "#if FOO";
        Preprocessor preprocessor = new();

        string result = preprocessor.Process(input, "");

        Assert.Equal(input, result);
    }
}
