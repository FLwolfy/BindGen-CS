using System;
using System.Threading;
using BGCS.Core.Inputs;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class FrontendLanguageBoundaryTests : FrontendTestHost
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void UnsupportedLanguagesFailBeforeParserActivation(int language)
    {
        FrontendRequest request = new(new NativeTargetDescriptor(new("language-boundary"), "test", "test", "test", "x86_64-pc-linux-gnu", new()),
            (SourceLanguage)language, ["unused.hpp"])
        { pathBase = AppContext.BaseDirectory };
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => new ClangFrontend(host).ParseAsync(request, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("Only C and C++", failure.Message);
    }
}
