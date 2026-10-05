using System;
using System.IO;
using System.Text;
using BGCS.Core.Text;
using Xunit;

namespace BGCS.Core.Tests;

public class FileNameHelperTests
{
    [Theory]
    [InlineData("a*b", "aStarb")]
    [InlineData("a:b", "aColonb")]
    [InlineData("a?b", "aQuestionMarkb")]
    [InlineData("a\"b", "aQuoteb")]
    public void SanitizeFileName_ShouldReplaceKnownCharacters(string input, string expected)
    {
        string actual = FileNameHelper.SanitizeFileName(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("prn", "_prn")]
    [InlineData("LPT1", "_LPT1")]
    public void SanitizeFileName_ReservedNames_ShouldPrefixUnderscore(string input, string expected)
    {
        string actual = FileNameHelper.SanitizeFileName(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SanitizeFileName_TooLongName_ShouldBeTrimmedTo255()
    {
        string input = new('a', 300);
        string actual = FileNameHelper.SanitizeFileName(input);

        Assert.Equal(255, actual.Length);
        Assert.Equal(new('a', 255), actual);
    }

    [Fact]
    public void SanitizeFileName_PathSeparators_ShouldRemain()
    {
        string input = "folder/sub\\name";
        string actual = FileNameHelper.SanitizeFileName(input);

        Assert.Contains("/", actual);
        Assert.Contains("\\", actual);
    }

    [Fact]
    public void SanitizeFileName_AbsolutePath_ShouldPreserveDirectoryAndSanitizeLeafName()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-path-test");
        string input = Path.Combine(directory, "a?b.cs");

        string actual = FileNameHelper.SanitizeFileName(input);

        Assert.Equal(Path.Combine(directory, "aQuestionMarkb.cs"), actual);
    }

    [Theory]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("nul .cs", "_nul .cs")]
    [InlineData("COM¹.txt", "_COM¹.txt")]
    [InlineData("name. ", "name")]
    [InlineData("..", "_")]
    [InlineData("name\u0001.cs", "name_.cs")]
    public void SanitizeFileName_UsesPortableRulesRegardlessOfHost(
        string input,
        string expected
    ) => Assert.Equal(expected, FileNameHelper.SanitizeFileName(input));

    [Fact]
    public void SanitizeFileName_UnicodeLimitPreservesScalarsAndThePortableByteBudget()
    {
        string input = new string('a', 252) + "😀";
        string actual = FileNameHelper.SanitizeFileName(input);

        Assert.Equal(new string('a', 252), actual);
        Assert.True(Encoding.UTF8.GetByteCount(actual) <= 255);
        Assert.Throws<ArgumentException>(() => FileNameHelper.SanitizeFileName("bad\uD800"));
    }
}
