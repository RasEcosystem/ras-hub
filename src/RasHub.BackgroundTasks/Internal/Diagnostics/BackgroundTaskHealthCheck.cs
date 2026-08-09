using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Configuration;

namespace RasHub.BackgroundTasks.Internal.Diagnostics;

internal sealed class BackgroundTaskHealthCheck : IHealthCheck
{
    private const double DegradedThreshold = 0.8;

    private readonly IBackgroundTaskEngine _engine;
    private readonly BackgroundTaskEngineOptions _options;

    public BackgroundTaskHealthCheck(
        IBackgroundTaskEngine engine,
        IOptions<BackgroundTaskEngineOptions> options)
    {
        _engine = engine;
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var statistics = _engine.GetStatistics();
        var data = new Dictionary<string, object>
        {
            ["activeTasks"] = statistics.ActiveTasks,
            ["maxActiveTasks"] = _options.MaxActiveTasks,
            ["completedTaskHistory"] = statistics.CompletedTaskHistory,
            ["maxCompletedTaskHistory"] = _options.MaxCompletedTaskHistory,
            ["interactiveQueueLength"] = statistics.InteractiveQueueLength,
            ["interactiveQueueCapacity"] = _options.InteractiveQueueCapacity,
            ["synchronizationQueueLength"] = statistics.SynchronizationQueueLength,
            ["synchronizationQueueCapacity"] = _options.SynchronizationQueueCapacity,
            ["maintenanceQueueLength"] = statistics.MaintenanceQueueLength,
            ["maintenanceQueueCapacity"] = _options.MaintenanceQueueCapacity
        };

        var saturated = statistics.ActiveTasks >= _options.MaxActiveTasks ||
                        statistics.InteractiveQueueLength >= _options.InteractiveQueueCapacity ||
                        statistics.SynchronizationQueueLength >= _options.SynchronizationQueueCapacity ||
                        statistics.MaintenanceQueueLength >= _options.MaintenanceQueueCapacity;

        if (saturated)
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Background task engine capacity is exhausted.",
                data: data));

        var degraded = IsAboveThreshold(statistics.ActiveTasks, _options.MaxActiveTasks) ||
                       IsAboveThreshold(
                           statistics.InteractiveQueueLength,
                           _options.InteractiveQueueCapacity) ||
                       IsAboveThreshold(
                           statistics.SynchronizationQueueLength,
                           _options.SynchronizationQueueCapacity) ||
                       IsAboveThreshold(
                           statistics.MaintenanceQueueLength,
                           _options.MaintenanceQueueCapacity);

        return Task.FromResult(degraded
            ? HealthCheckResult.Degraded(
                "Background task engine is approaching its capacity.",
                data: data)
            : HealthCheckResult.Healthy(
                "Background task engine is operational.",
                data));
    }

    private static bool IsAboveThreshold(int value, int capacity)
    {
        return (double)value / capacity >= DegradedThreshold;
    }
}