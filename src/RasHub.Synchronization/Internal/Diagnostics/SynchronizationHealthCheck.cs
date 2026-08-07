using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Configuration;

namespace RasHub.Synchronization.Internal.Diagnostics;

internal sealed class SynchronizationHealthCheck : IHealthCheck
{
    private const double DegradedThreshold = 0.8;

    private readonly ISynchronizationEngine _engine;
    private readonly SynchronizationEngineOptions _options;

    public SynchronizationHealthCheck(
        ISynchronizationEngine engine,
        IOptions<SynchronizationEngineOptions> options)
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
            ["trackedTasks"] = statistics.TrackedTasks,
            ["maxTrackedTasks"] = _options.MaxTrackedTasks,
            ["interactiveQueueLength"] = statistics.InteractiveQueueLength,
            ["interactiveQueueCapacity"] = _options.InteractiveQueueCapacity,
            ["synchronizationQueueLength"] = statistics.SynchronizationQueueLength,
            ["synchronizationQueueCapacity"] = _options.QueueCapacity,
            ["maintenanceQueueLength"] = statistics.MaintenanceQueueLength,
            ["maintenanceQueueCapacity"] = _options.MaintenanceQueueCapacity
        };

        var saturated = statistics.TrackedTasks >= _options.MaxTrackedTasks ||
                        statistics.InteractiveQueueLength >= _options.InteractiveQueueCapacity ||
                        statistics.SynchronizationQueueLength >= _options.QueueCapacity ||
                        statistics.MaintenanceQueueLength >= _options.MaintenanceQueueCapacity;

        if (saturated)
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Synchronization Engine capacity is exhausted.",
                data: data));

        var degraded = IsAboveThreshold(statistics.TrackedTasks, _options.MaxTrackedTasks) ||
                       IsAboveThreshold(
                statistics.InteractiveQueueLength,
                _options.InteractiveQueueCapacity) ||
                       IsAboveThreshold(
                statistics.SynchronizationQueueLength,
                _options.QueueCapacity) ||
                       IsAboveThreshold(
                statistics.MaintenanceQueueLength,
                _options.MaintenanceQueueCapacity);

        return Task.FromResult(degraded
            ? HealthCheckResult.Degraded(
                "Synchronization Engine is approaching its capacity.",
                data: data)
            : HealthCheckResult.Healthy(
                "Synchronization Engine is operational.",
                data));
    }

    private static bool IsAboveThreshold(int value, int capacity)
    {
        return (double)value / capacity >= DegradedThreshold;
    }
}
