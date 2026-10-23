using System;
using System.Threading;
using BGCS.Core.Inputs;
using BGCS.Core.Requests;
using BGCS.Targets.Desktop.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class FrontendLayoutOptionTests : FrontendTestHost
{
    [Theory]
    [InlineData("-funsigned-bitfields")]
    [InlineData("/clang:-funsigned-bitfields")]
    public void UnsupportedPlainBitfieldSignednessCannotBecomeADetachedDefaultFact(string option)
    {
        FrontendRequest request = new(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.C, ["header-must-not-be-opened.h"]) { arguments = [option], pathBase = AppContext.BaseDirectory };
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() =>
        {
            _ = new ClangFrontend(host).ParseAsync(request, CancellationToken.None);
        });
        Assert.Contains("unsigned plain-int bitfield semantics", failure.Message);
    }
}
