using BGCS.Runtime;
using BGCS.Runtime.COM;
using Xunit;

namespace BGCS.Runtime.COM.Tests;

public class GeometryTests
{
    [Fact]
    public void Point32_Equality_ShouldWork()
    {
        Point32 a = new(10, 20);
        Point32 b = new(10, 20);
        Point32 c = new(20, 10);

        Assert.True(a == b);
        Assert.True(a != c);
    }

    [Fact]
    public void Rect32_Equality_ShouldWork()
    {
        Rect32 a = new(1, 2, 3, 4);
        Rect32 b = new(1, 2, 3, 4);
        Rect32 c = new(0, 2, 3, 4);

        Assert.True(a == b);
        Assert.True(a != c);
    }

    [Fact]
    public void Point32_HashCode_EqualValuesShouldMatch()
    {
        Point32 a = new(7, 9);
        Point32 b = new(7, 9);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Rect32_HashCode_DifferentValuesShouldDiffer()
    {
        Rect32 a = new(1, 2, 3, 4);
        Rect32 b = new(1, 2, 3, 5);

        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void HResult_CreateAndExtract_ShouldMatchInputParts()
    {
        HResult hr = new(severity: 1, facility: 7, code: 42);

        Assert.Equal(1, hr.Severity);
        Assert.Equal(7, hr.Facility);
        Assert.Equal(42, hr.Code);
        Assert.True(hr.IsFailure);
        Assert.True(hr.IsError);
        Assert.False(hr.IsSuccess);
    }

    [Fact]
    public void HResult_SuccessAndFailureChecks_ShouldBeConsistent()
    {
        HResult ok = new(0);
        HResult fail = new(unchecked((int)0x80004005));

        Assert.True(ok.IsSuccess);
        Assert.False(ok.IsFailure);

        Assert.False(fail.IsSuccess);
        Assert.True(fail.IsFailure);
        Assert.True(fail.IsError);
    }
}
