using System;
using System.Collections.Generic;
using BGCS.Core.Logging;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class LoggerTests
{
    [Fact]
    public void Verbosity_FiltersNotificationsWithoutDiscardingResultDiagnostics()
    {
        var logger = new OperationLogger { logLevel = LogSeverity.Critical };
        List<LogMessage> notifications = [];
        logger.LogEvent += (
            severity,
            message
        ) => notifications.Add(new(severity, message));
        logger.LogError("parser failed");
        logger.LogCritical("cannot continue");

        Assert.Equal(2, logger.messages.Count);
        Assert.Equal(LogSeverity.Error, logger.messages[0].severity);
        Assert.Single(notifications);
        Assert.Equal(LogSeverity.Critical, notifications[0].severity);
    }

    [Fact]
    public void DeduplicationAndReset_AreScopedToEachOperation()
    {
        var logger = new OperationLogger();
        int notifications = 0;
        logger.LogEvent += (
            _,
            _
        ) => notifications++;
        logger.LogWarn("same issue");
        logger.LogWarn("same issue");
        logger.LogError("same issue");
        Assert.Equal(2, logger.messages.Count);
        Assert.Equal(2, notifications);

        logger.Start();
        Assert.Empty(logger.messages);
        logger.LogWarn("same issue");
        Assert.Single(logger.messages);
        Assert.Equal(3, notifications);
    }

    [Fact]
    public void CapturedDiagnostics_CannotBeChangedThroughTheExposedCollection()
    {
        var logger = new OperationLogger();
        logger.LogInfo("progress");
        var collection = Assert.IsAssignableFrom<ICollection<LogMessage>>(logger.messages);

        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Single(logger.messages);
    }

    [Fact]
    public void SourceThreshold_ChangesOnlyLiveNotifications()
    {
        var logger = new OperationLogger();
        int notifications = 0;
        logger.LogEvent += (
            _,
            _
        ) => notifications++;
        logger.Capture(LogSeverity.Warning, "parser warning", LogSeverity.Error);
        logger.Capture(LogSeverity.Error, "parser error", LogSeverity.Error);

        Assert.Equal(2, logger.messages.Count);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void InvalidDiagnostics_FailBeforeChangingTheCapturedState()
    {
        var logger = new OperationLogger();
        Assert.Throws<ArgumentNullException>(() => logger.LogInfo(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => logger.Log((LogSeverity)99, "bad"));
        Assert.Throws<ArgumentOutOfRangeException>(() => logger.logLevel = (LogSeverity)99);
        Assert.Empty(logger.messages);
    }

    private sealed class OperationLogger : LoggerBase
    {
        public void Start() => ResetDiagnostics();

        public void Capture(
            LogSeverity severity,
            string message,
            LogSeverity threshold
        ) => RecordDiagnostic(severity, message, threshold);
    }
}
