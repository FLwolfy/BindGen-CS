using System.Runtime.CompilerServices;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class NativeLongDoubleTests
{
    [Fact]
    public void NativeLongDoubleRepresentations_ShouldPreserveAbiSize()
    {
        Assert.Equal(12, Unsafe.SizeOf<NativeLongDouble12>());
        Assert.Equal(16, Unsafe.SizeOf<NativeLongDouble16>());
    }
}
