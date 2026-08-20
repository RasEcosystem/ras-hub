using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace RasHub.Web.Infrastructure.Diagnostics;

public sealed class ApplicationDiagnostics(TimeProvider timeProvider) : ILogEventSink
{
    public const int DefaultDisplayedHourCount = 24;
    public const int RetainedEventCapacity = 5_000;
    private const int RetainedHourCount = 48;
    private const int MaximumMessageLength = 4_000;
    private const int MaximumExceptionDetailsLength = 16_000;
    private readonly string?[] _eventCorrelationKeys = new string?[RetainedEventCapacity];

    private readonly Dictionary<string, int> _eventIndexesByCorrelationKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ApplicationDiagnosticEvent?[] _events =
        new ApplicationDiagnosticEvent[RetainedEventCapacity];

    private readonly object _eventsLock = new();
    private readonly HourCounters?[] _hourCounters = new HourCounters[RetainedHourCount];
    private readonly object _hourCountersLock = new();
    private long _errorCount;
    private int _eventCount;
    private int _eventStart;
    private long _nextEventId;
    private long _warningCount;

    public DateTimeOffset StartedAt { get; } = timeProvider.GetUtcNow();

    public long WarningCount => Interlocked.Read(ref _warningCount);

    public long ErrorCount => Interlocked.Read(ref _errorCount);

    public int RetainedEventCount
    {
        get
        {
            lock (_eventsLock)
            {
                return _eventCount;
            }
        }
    }

    public void Emit(LogEvent logEvent)
    {
        switch (logEvent.Level)
        {
            case LogEventLevel.Warning:
            case LogEventLevel.Error:
            case LogEventLevel.Fatal:
                var change = StoreEvent(logEvent);
                ApplyCounterChange(change.Previous, change.Current);
                break;
        }
    }

    public IReadOnlyList<ApplicationDiagnosticEvent> GetEvents(
        DateTimeOffset? fromInclusive = null,
        DateTimeOffset? toExclusive = null)
    {
        lock (_eventsLock)
        {
            var result = new List<ApplicationDiagnosticEvent>(_eventCount);

            for (var offset = _eventCount - 1; offset >= 0; offset--)
            {
                var index = (_eventStart + offset) % RetainedEventCapacity;
                var diagnosticEvent = _events[index]!;

                if (fromInclusive is not null &&
                    diagnosticEvent.Timestamp < fromInclusive.Value)
                    continue;

                if (toExclusive is not null &&
                    diagnosticEvent.Timestamp >= toExclusive.Value)
                    continue;

                result.Add(diagnosticEvent);
            }

            result.Sort(static (left, right) =>
                right.Timestamp.CompareTo(left.Timestamp));
            return result;
        }
    }

    public IReadOnlyList<ApplicationHealthHour> GetHourlyHealth(
        DateTimeOffset now,
        int hourCount = DefaultDisplayedHourCount)
    {
        if (hourCount is < 1 or > RetainedHourCount)
            throw new ArgumentOutOfRangeException(
                nameof(hourCount),
                $"Hour count must be between 1 and {RetainedHourCount}.");

        var currentHour = GetHourStart(now);
        var firstHour = currentHour.AddHours(1 - hourCount);
        var result = new ApplicationHealthHour[hourCount];

        lock (_hourCountersLock)
        {
            for (var index = 0; index < hourCount; index++)
            {
                var hour = firstHour.AddHours(index);
                var counters = _hourCounters[GetSlot(hour)];
                var hasCounters = counters?.StartedAt == hour;

                result[index] = new ApplicationHealthHour(
                    hour,
                    hasCounters ? counters!.WarningCount : 0,
                    hasCounters ? counters!.ErrorCount : 0,
                    StartedAt < hour.AddHours(1) && hour <= now);
            }
        }

        return result;
    }

    private void ApplyCounterChange(
        ApplicationDiagnosticEvent? previous,
        ApplicationDiagnosticEvent current)
    {
        var warningDelta = IsWarning(current.Level) ? 1 : 0;
        var errorDelta = IsError(current.Level) ? 1 : 0;

        if (previous is not null)
        {
            warningDelta -= IsWarning(previous.Level) ? 1 : 0;
            errorDelta -= IsError(previous.Level) ? 1 : 0;
        }

        if (warningDelta != 0)
            Interlocked.Add(ref _warningCount, warningDelta);

        if (errorDelta != 0)
            Interlocked.Add(ref _errorCount, errorDelta);

        var currentHour = GetHourStart(timeProvider.GetUtcNow());

        lock (_hourCountersLock)
        {
            if (previous is not null)
                ApplyHourDelta(previous, -1, currentHour);

            ApplyHourDelta(current, 1, currentHour);
        }
    }

    private void ApplyHourDelta(
        ApplicationDiagnosticEvent diagnosticEvent,
        int delta,
        DateTimeOffset currentHour)
    {
        var hour = GetHourStart(diagnosticEvent.Timestamp);

        if (hour < currentHour.AddHours(1 - RetainedHourCount) ||
            hour > currentHour)
            return;

        var slot = GetSlot(hour);
        var counters = _hourCounters[slot];

        if (counters?.StartedAt != hour)
        {
            if (delta < 0)
                return;

            counters = new HourCounters(hour);
            _hourCounters[slot] = counters;
        }

        if (IsError(diagnosticEvent.Level))
            counters.ErrorCount = Math.Max(0, counters.ErrorCount + delta);
        else
            counters.WarningCount = Math.Max(0, counters.WarningCount + delta);
    }

    private StoredEventChange StoreEvent(LogEvent logEvent)
    {
        var diagnosticEvent = new ApplicationDiagnosticEvent(
            0,
            logEvent.Timestamp,
            logEvent.Level,
            Truncate(logEvent.RenderMessage(CultureInfo.InvariantCulture), MaximumMessageLength),
            GetPropertyValue(logEvent, "SourceContext"),
            GetFirstPropertyValue(logEvent, "TraceId", "RequestId", "CorrelationId"),
            logEvent.Exception?.GetType().FullName,
            logEvent.Exception is null
                ? null
                : Truncate(logEvent.Exception.Message, MaximumMessageLength),
            logEvent.Exception is null
                ? null
                : Truncate(logEvent.Exception.ToString(), MaximumExceptionDetailsLength));
        var correlationKey = GetCorrelationKey(logEvent);

        lock (_eventsLock)
        {
            if (correlationKey is not null &&
                _eventIndexesByCorrelationKey.TryGetValue(correlationKey, out var existingIndex) &&
                _events[existingIndex] is { } previous)
            {
                diagnosticEvent = diagnosticEvent with { Id = previous.Id };
                _events[existingIndex] = diagnosticEvent;
                return new StoredEventChange(previous, diagnosticEvent);
            }

            diagnosticEvent = diagnosticEvent with { Id = Interlocked.Increment(ref _nextEventId) };
            var index = (_eventStart + _eventCount) % RetainedEventCapacity;

            if (_eventCount == RetainedEventCapacity)
            {
                index = _eventStart;
                _eventStart = (_eventStart + 1) % RetainedEventCapacity;

                if (_eventCorrelationKeys[index] is { } previousCorrelationKey &&
                    _eventIndexesByCorrelationKey.TryGetValue(
                        previousCorrelationKey,
                        out var mappedIndex) &&
                    mappedIndex == index)
                    _eventIndexesByCorrelationKey.Remove(previousCorrelationKey);
            }
            else
            {
                _eventCount++;
            }

            _events[index] = diagnosticEvent;
            _eventCorrelationKeys[index] = correlationKey;

            if (correlationKey is not null)
                _eventIndexesByCorrelationKey[correlationKey] = index;

            return new StoredEventChange(null, diagnosticEvent);
        }
    }

    private static string? GetCorrelationKey(LogEvent logEvent)
    {
        var sourceContext = GetPropertyValue(logEvent, "SourceContext");

        if (sourceContext?.EndsWith(
                ".BackgroundTaskWorker",
                StringComparison.Ordinal) != true)
            return null;

        var taskId = GetPropertyValue(logEvent, "TaskId");
        return string.IsNullOrWhiteSpace(taskId)
            ? null
            : $"background-task:{taskId}";
    }

    private static bool IsWarning(LogEventLevel level)
    {
        return level == LogEventLevel.Warning;
    }

    private static bool IsError(LogEventLevel level)
    {
        return level is LogEventLevel.Error or LogEventLevel.Fatal;
    }

    private static string? GetFirstPropertyValue(
        LogEvent logEvent,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetPropertyValue(logEvent, propertyName);

            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? GetPropertyValue(LogEvent logEvent, string propertyName)
    {
        if (!logEvent.Properties.TryGetValue(propertyName, out var propertyValue))
            return null;

        return propertyValue is ScalarValue { Value: not null } scalar
            ? Convert.ToString(scalar.Value, CultureInfo.InvariantCulture)
            : propertyValue.ToString().Trim('"');
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength
            ? value
            : string.Concat(value.AsSpan(0, maximumLength - 1), "…");
    }

    private static DateTimeOffset GetHourStart(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new DateTimeOffset(
            utc.Year,
            utc.Month,
            utc.Day,
            utc.Hour,
            0,
            0,
            TimeSpan.Zero);
    }

    private static int GetSlot(DateTimeOffset hour)
    {
        var hourNumber = hour.UtcDateTime.Ticks / TimeSpan.TicksPerHour;
        return (int)(hourNumber % RetainedHourCount);
    }

    private sealed class HourCounters(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;

        public long WarningCount { get; set; }

        public long ErrorCount { get; set; }
    }

    private sealed record StoredEventChange(
        ApplicationDiagnosticEvent? Previous,
        ApplicationDiagnosticEvent Current);
}

public sealed record ApplicationHealthHour(
    DateTimeOffset StartedAt,
    long WarningCount,
    long ErrorCount,
    bool HasData);

public sealed record ApplicationDiagnosticEvent(
    long Id,
    DateTimeOffset Timestamp,
    LogEventLevel Level,
    string Message,
    string? SourceContext,
    string? TraceId,
    string? ExceptionType,
    string? ExceptionMessage,
    string? ExceptionDetails);