using System;
using System.Collections.Generic;
using BGCS.Language.Diagnostics;
using Xunit;

namespace BGCS.Language.Tests;

public sealed class DiagnosticOwnershipTests
{
    [Fact]
    public void DiagnosticViewRejectsMutationAndReflectsAppends()
    {
        DiagnosticBag bag = new();
        IReadOnlyList<DiagnosticMessage> view = bag.messages;
        bag.Error("error");
        Assert.Single(view);
        Assert.Throws<NotSupportedException>(() => ((IList<DiagnosticMessage>)view).Clear());
        Assert.True(bag.hasErrors);
        Assert.Single(bag.messages);
    }

    [Fact]
    public void CopyRejectsSelfBeforeMutationAndPreservesDestinationEntries()
    {
        DiagnosticBag source = new();
        source.Error("error");
        Assert.Throws<ArgumentException>(() => source.CopyTo(source));
        Assert.Single(source.messages);
        DiagnosticBag destination = new();
        destination.Info("first");
        source.CopyTo(destination);
        Assert.Equal(2, destination.messages.Count);
        Assert.True(destination.hasErrors);
    }
}
