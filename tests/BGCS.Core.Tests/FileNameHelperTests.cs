using System;
using BGCS.Core;
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
}
