using RasHub.Web.Infrastructure.Diagnostics;
using Serilog.Events;

namespace RasHub.Web.IntegrationTests.Diagnostics;

public sealed class ApplicationDiagnosticsTests
{
    private static readonly MessageTemplate TestMessage = new("test", []);

    [Fact]
    public void Hourly_health_tracks_severity_and_distinguishes_missing_data()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 9, 9, 5, 0, TimeSpan.Zero));
        var diagnostics = new ApplicationDiagnostics(timeProvider);
        timeProvider.UtcNow = new DateTimeOffset(
            2026, 8, 9, 12, 30, 0, TimeSpan.Zero);

        diagnostics.Emit(CreateEvent(
            new DateTimeOffset(2026, 8, 9, 10, 10, 0, TimeSpan.Zero),
            LogEventLevel.Warning));
        diagnostics.Emit(CreateEvent(
            new DateTimeOffset(2026, 8, 9, 11, 20, 0, TimeSpan.Zero),
            LogEventLevel.Error));
        diagnostics.Emit(CreateEvent(
            new DateTimeOffset(2026, 8, 9, 11, 25, 0, TimeSpan.Zero),
            LogEventLevel.Fatal));
        diagnostics.Emit(CreateEvent(timeProvider.UtcNow, LogEventLevel.Information));

        var hours = diagnostics.GetHourlyHealth(timeProvider.UtcNow, 5);

        Assert.False(hours[0].HasData);
        Assert.True(hours[1].HasData);
        Assert.Equal(1, hours[2].WarningCount);
        Assert.Equal(2, hours[3].ErrorCount);
        Assert.True(hours[4].HasData);
        Assert.Equal(1, diagnostics.WarningCount);
        Assert.Equal(2, diagnostics.ErrorCount);
    }

    [Fact]
    public void Events_store_only_actionable_levels_and_support_hour_filtering()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 9, 12, 30, 0, TimeSpan.Zero));
        var diagnostics = new ApplicationDiagnostics(timeProvider);
        var warning = CreateEvent(
            new DateTimeOffset(2026, 8, 9, 10, 10, 0, TimeSpan.Zero),
            LogEventLevel.Warning);
        warning.AddPropertyIfAbsent(new LogEventProperty(
            "SourceContext",
            new ScalarValue("RasHub.Test.Worker")));
        warning.AddPropertyIfAbsent(new LogEventProperty(
            "TraceId",
            new ScalarValue("trace-123")));
        var error = CreateEvent(
            new DateTimeOffset(2026, 8, 9, 10, 20, 0, TimeSpan.Zero),
            LogEventLevel.Error,
            new InvalidOperationException("Operation failed."));

        diagnostics.Emit(warning);
        diagnostics.Emit(error);
        diagnostics.Emit(CreateEvent(
            new DateTimeOffset(2026, 8, 9, 10, 30, 0, TimeSpan.Zero),
            LogEventLevel.Information));
        diagnostics.Emit(CreateEvent(
            new DateTimeOffset(2026, 8, 9, 11, 0, 0, TimeSpan.Zero),
            LogEventLevel.Fatal));

        var events = diagnostics.GetEvents(
            new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 9, 11, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, events.Count);
        Assert.Equal(LogEventLevel.Error, events[0].Level);
        Assert.Equal("Operation failed.", events[0].ExceptionMessage);
        Assert.Contains(nameof(InvalidOperationException), events[0].ExceptionDetails);
        Assert.Equal(LogEventLevel.Warning, events[1].Level);
        Assert.Equal("RasHub.Test.Worker", events[1].SourceContext);
        Assert.Equal("trace-123", events[1].TraceId);
        Assert.Equal(3, diagnostics.RetainedEventCount);
    }

    [Fact]
    public void Event_storage_is_bounded_and_keeps_newest_events()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 9, 12, 30, 0, TimeSpan.Zero));
        var diagnostics = new ApplicationDiagnostics(timeProvider);

        for (var index = 0;
             index < ApplicationDiagnostics.RetainedEventCapacity + 1;
             index++)
            diagnostics.Emit(CreateEvent(
                timeProvider.UtcNow.AddTicks(index),
                LogEventLevel.Warning));

        var events = diagnostics.GetEvents();

        Assert.Equal(ApplicationDiagnostics.RetainedEventCapacity, events.Count);
        Assert.Equal(ApplicationDiagnostics.RetainedEventCapacity + 1, events[0].Id);
        Assert.Equal(2, events[^1].Id);
    }

    [Fact]
    public void Background_task_retries_are_aggregated_into_one_terminal_event()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 9, 12, 30, 0, TimeSpan.Zero));
        var diagnostics = new ApplicationDiagnostics(timeProvider);
        var taskId = Guid.NewGuid();
        var firstAttempt = CreateBackgroundTaskEvent(
            timeProvider.UtcNow.AddMinutes(-2),
            LogEventLevel.Warning,
            taskId,
            new InvalidOperationException("First attempt failed."));
        var secondAttempt = CreateBackgroundTaskEvent(
            timeProvider.UtcNow.AddMinutes(-1),
            LogEventLevel.Warning,
            taskId,
            new InvalidOperationException("Second attempt failed."));
        var terminalFailure = CreateBackgroundTaskEvent(
            timeProvider.UtcNow,
            LogEventLevel.Error,
            taskId,
            new InvalidOperationException("Failed permanently."));

        diagnostics.Emit(firstAttempt);
        diagnostics.Emit(secondAttempt);
        diagnostics.Emit(terminalFailure);

        var diagnosticEvent = Assert.Single(diagnostics.GetEvents());
        var hour = Assert.Single(diagnostics.GetHourlyHealth(timeProvider.UtcNow, 1));

        Assert.Equal(1, diagnosticEvent.Id);
        Assert.Equal(LogEventLevel.Error, diagnosticEvent.Level);
        Assert.Equal("Failed permanently.", diagnosticEvent.ExceptionMessage);
        Assert.Equal(0, diagnostics.WarningCount);
        Assert.Equal(1, diagnostics.ErrorCount);
        Assert.Equal(0, hour.WarningCount);
        Assert.Equal(1, hour.ErrorCount);
    }

    private static LogEvent CreateEvent(
        DateTimeOffset timestamp,
        LogEventLevel level,
        Exception? exception = null)
    {
        return new LogEvent(timestamp, level, exception, TestMessage, []);
    }

    private static LogEvent CreateBackgroundTaskEvent(
        DateTimeOffset timestamp,
        LogEventLevel level,
        Guid taskId,
        Exception exception)
    {
        var logEvent = CreateEvent(timestamp, level, exception);
        logEvent.AddPropertyIfAbsent(new LogEventProperty(
            "SourceContext",
            new ScalarValue(
                "RasHub.Synchronization.Internal.Processing.BackgroundTaskWorker")));
        logEvent.AddPropertyIfAbsent(new LogEventProperty(
            "TaskId",
            new ScalarValue(taskId)));
        return logEvent;
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
        }
    }
}