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
    private readonly BackgroundTaskRuntimeState _runtimeState;

    public BackgroundTaskHealthCheck(
        IBackgroundTaskEngine engine,
        IOptions<BackgroundTaskEngineOptions> options,
        BackgroundTaskRuntimeState runtimeState)
    {
        _engine = engine;
        _options = options.Value;
        _runtimeState = runtimeState;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var statistics = _engine.GetStatistics();
        var runtime = _runtimeState.CreateSnapshot();
        var data = new Dictionary<string, object>
        {
            ["runtimeStatus"] = runtime.Status.ToString(),
            ["expectedProcessCount"] = runtime.ExpectedProcessCount,
            ["liveProcessCount"] = runtime.LiveProcessCount,
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

        if (runtime.FaultedProcess is not null)
            data["faultedProcess"] = runtime.FaultedProcess;

        if (runtime.FaultedAt is not null)
            data["faultedAt"] = runtime.FaultedAt.Value;

        var runtimeUnavailable = runtime.Status is
                                     BackgroundTaskRuntimeStatus.Faulted or
                                     BackgroundTaskRuntimeStatus.Stopping or
                                     BackgroundTaskRuntimeStatus.Stopped ||
                                 runtime.Status == BackgroundTaskRuntimeStatus.Running &&
                                 runtime.LiveProcessCount != runtime.ExpectedProcessCount;

        if (runtimeUnavailable)
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Background task engine processes are not operational.",
                data: data));

        var saturated = statistics.ActiveTasks >= _options.MaxActiveTasks ||
                        statistics.InteractiveQueueLength >= _options.InteractiveQueueCapacity ||
                        statistics.SynchronizationQueueLength >= _options.SynchronizationQueueCapacity ||
                        statistics.MaintenanceQueueLength >= _options.MaintenanceQueueCapacity;

        if (saturated)
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Background task engine capacity is exhausted.",
                data: data));

        if (runtime.Status is
            BackgroundTaskRuntimeStatus.NotStarted or
            BackgroundTaskRuntimeStatus.Starting)
            return Task.FromResult(HealthCheckResult.Degraded(
                "Background task engine has not finished starting.",
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
