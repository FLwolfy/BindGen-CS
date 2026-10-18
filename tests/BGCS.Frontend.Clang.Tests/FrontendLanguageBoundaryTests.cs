using System;
using System.Threading;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class FrontendLanguageBoundaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void UnsupportedLanguagesFailBeforeParserActivation(int language)
    {
        FrontendRequest request = new(new NativeTargetDescriptor(new("language-boundary"), "test", "test", "test", "x86_64-pc-linux-gnu", new()),
            (SourceLanguage)language, ["unused.hpp"]);
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => new ClangFrontend().ParseAsync(request, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("Only C and C++", failure.Message);
    }
}
