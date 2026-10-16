using System;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class CallbackExceptionBoundaryTests
{
    [Fact]
    public void MultipleInstances_DoNotShareCallbackErrors()
    {
        CallbackExceptionBoundary first = new();
        CallbackExceptionBoundary second = new();
        Assert.False(first.Invoke(() => throw new InvalidOperationException("first")));
        Assert.Null(second.TakeException());
        Assert.Equal("first", first.TakeException()!.Message);
    }

    [Fact]
    public void RepeatedFailures_HaveBoundedRetentionAndReportDroppedErrors()
    {
        CallbackExceptionBoundary boundary = new(2);
        for (int index = 0; index < 10; index++)
            boundary.Invoke(() => throw new InvalidOperationException(index.ToString()));
        Assert.Equal(8, boundary.droppedErrorCount);
        Assert.Equal("8", boundary.TakeException()!.Message);
        Assert.Equal("9", boundary.TakeException()!.Message);
        Assert.Null(boundary.TakeException());
    }

    [Fact]
    public async Task CallbacksOnDifferentThreads_ReportToTheirOwningBoundary()
    {
        CallbackExceptionBoundary boundary = new();
        await Task.WhenAll(Task.Run(() => boundary.Invoke(() => throw new InvalidOperationException("a"))),
            Task.Run(() => boundary.Invoke(() => throw new InvalidOperationException("b"))));
        Assert.NotNull(boundary.TakeException());
        Assert.NotNull(boundary.TakeException());
        Assert.Null(boundary.TakeException());
    }
}
