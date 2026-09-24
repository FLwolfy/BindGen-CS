using BGCS.Core.CSharp;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class CsTypeTests
{
    [Theory]
    [InlineData("int")]
    [InlineData("nuint")]
    [InlineData("byte*")]
    [InlineData("void **")]
    public void IsKnownPrimitive_RecognizesOnlyWholeBuiltInTypeNames(string typeName)
    {
        Assert.True(CsType.IsKnownPrimitive(typeName));
    }

    [Theory]
    [InlineData("intensity")]
    [InlineData("boolean")]
    [InlineData("Vector2")]
    [InlineData("Vector3Custom")]
    [InlineData("ExternalVector4")]
    public void IsKnownPrimitive_DoesNotAssumeExternalOrPrefixTypes(string typeName)
    {
        Assert.False(CsType.IsKnownPrimitive(typeName));
    }
}
