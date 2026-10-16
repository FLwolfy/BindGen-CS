using System;
using System.Threading;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class FrontendLayoutOptionTests
{
    [Theory]
    [InlineData("-funsigned-bitfields")]
    [InlineData("/clang:-funsigned-bitfields")]
    public void UnsupportedPlainBitfieldSignednessCannotBecomeADetachedDefaultFact(string option)
    {
        FrontendRequest request = new(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.C, ["header-must-not-be-opened.h"]) { arguments = [option] };
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() =>
        {
            _ = new ClangFrontend().ParseAsync(request, CancellationToken.None);
        });
        Assert.Contains("unsigned plain-int bitfield semantics", failure.Message);
    }
}
