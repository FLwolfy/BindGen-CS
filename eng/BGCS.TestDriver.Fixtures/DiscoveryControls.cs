using Xunit;

namespace BGCS.TestDriverControls;

public abstract class InheritedFacts
{
    [Fact]
    public void InheritedFact() => Assert.True(true);

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    public void InheritedTheory(int value) => Assert.True(value > 0);
}

public sealed class InheritedCases : InheritedFacts
{
}

public sealed class GenericCases
{
    [Theory]
    [InlineData(7)]
    [InlineData("literal")]
    public void GenericTheory<T>(T value) => Assert.NotNull(value);
}

public sealed class LiteralCases
{
    [Theory]
    [InlineData("a,(b):\"c\"", 1)]
    [InlineData("Ω", 2)]
    public void EscapedTheory(string value, int number)
    {
        Assert.NotEmpty(value);
        Assert.True(number > 0);
    }
}

public sealed class DerivedFactAttribute : FactAttribute
{
}

public sealed class DerivedAttributeCases
{
    [DerivedFact]
    public void DerivedFactIsIncluded() => Assert.True(true);
}

#if NEGATIVE_DYNAMIC
public sealed class DynamicCases
{
    public static System.Collections.Generic.IEnumerable<object[]> Cases => new[] { new object[] { 1 }, new object[] { 2 } };

    [Theory]
    [MemberData(nameof(Cases), DisableDiscoveryEnumeration = true)]
    public void CollapsedTheoryIsRejected(int value) => Assert.True(value > 0);
}
#endif

#if NEGATIVE_SKIPPED
public sealed class SkippedCases
{
    [Fact(Skip = "The strict driver must reject this skipped required case.")]
    public void SkippedFactIsRejected() => Assert.True(true);
}
#endif
