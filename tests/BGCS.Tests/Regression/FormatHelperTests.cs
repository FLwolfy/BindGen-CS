using Xunit;

namespace BGCS.Tests;

public class FormatHelperTests
{
    [Fact]
    public void NormalizeConstantValue_MultiSegmentWideString_ShouldFlattenToVerbatim()
    {
        string value = "L\"foo\"R\"bar\"";

        string normalized = value.NormalizeConstantValue();

        Assert.Equal("@\"foobar\"", normalized);
    }

    [Fact]
    public void NormalizeConstantValue_LRRawString_ShouldPreserveNewlines()
    {
        string value = "LR\"line1\nline2\"";

        string normalized = value.NormalizeConstantValue();

        Assert.Equal("@\"line1\nline2\"", normalized);
    }
}
