using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class Aapcs64VaListTests
{
    [Fact]
    public void Layout_ShouldMatchAapcs64()
    {
        Assert.Equal(32, Unsafe.SizeOf<Aapcs64VaList>());
        Assert.Equal(0, Marshal.OffsetOf<Aapcs64VaList>(nameof(Aapcs64VaList.stack)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<Aapcs64VaList>(nameof(Aapcs64VaList.generalRegisterTop)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<Aapcs64VaList>(nameof(Aapcs64VaList.vectorRegisterTop)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<Aapcs64VaList>(nameof(Aapcs64VaList.generalRegisterOffset)).ToInt32());
        Assert.Equal(28, Marshal.OffsetOf<Aapcs64VaList>(nameof(Aapcs64VaList.vectorRegisterOffset)).ToInt32());
    }
}
