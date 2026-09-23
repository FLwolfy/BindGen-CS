using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class Aapcs64VaListTests
{
    [Fact]
    public void Layout_ShouldMatchAapcs64()
    {
        Assert.Equal(32, Unsafe.SizeOf<Aapcs64VaList>());
        Assert.Equal(0, Marshal.OffsetOf<Aapcs64VaList>(nameof(Aapcs64VaList.Stack)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<Aapcs64VaList>(nameof(Aapcs64VaList.GeneralRegisterTop)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<Aapcs64VaList>(nameof(Aapcs64VaList.VectorRegisterTop)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<Aapcs64VaList>(nameof(Aapcs64VaList.GeneralRegisterOffset)).ToInt32());
        Assert.Equal(28, Marshal.OffsetOf<Aapcs64VaList>(nameof(Aapcs64VaList.VectorRegisterOffset)).ToInt32());
    }
}
