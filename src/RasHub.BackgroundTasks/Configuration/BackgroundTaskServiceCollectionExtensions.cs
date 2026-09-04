using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Internal.Diagnostics;
using RasHub.BackgroundTasks.Internal.Engine;
using RasHub.BackgroundTasks.Internal.Execution;
using RasHub.BackgroundTasks.Internal.Processing;
using RasHub.BackgroundTasks.Internal.Queues;
using RasHub.BackgroundTasks.Internal.Scheduling;

namespace RasHub.BackgroundTasks.Configuration;

/// <summary>Registers background task execution services.</summary>
public static class BackgroundTaskServiceCollectionExtensions
{
    public static IServiceCollection AddRasHubBackgroundTasks(
        this IServiceCollection services,
        Action<BackgroundTaskEngineOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<BackgroundTaskEngineOptions>();

        if (configure is not null)
            optionsBuilder.Configure(configure);

        optionsBuilder
            .Validate(
                options => options.InteractiveQueueCapacity > 0 &&
                           options.SynchronizationQueueCapacity > 0 &&
                           options.MaintenanceQueueCapacity > 0,
                "All background task queue capacities must be greater than zero.")
            .Validate(
                options => IsValidWorkerCount(options.InteractiveWorkerCount) &&
                           IsValidWorkerCount(options.SynchronizationWorkerCount) &&
                           IsValidWorkerCount(options.MaintenanceWorkerCount),
                $"Background task worker counts must be between 1 and " +
                $"{BackgroundTaskEngineOptions.MaximumWorkersPerQueue}.")
            .Validate(
                options => GetTotalWorkerCount(options) <=
                           BackgroundTaskEngineOptions.MaximumTotalWorkerCount,
                $"The total background task worker count must not exceed " +
                $"{BackgroundTaskEngineOptions.MaximumTotalWorkerCount}.")
            .Validate(
                options => options.CompletedTaskRetention >= TimeSpan.Zero,
                "Task retention cannot be negative.")
            .Validate(
                options => options.RegistryCleanupInterval >=
                           BackgroundTaskTimerLimits.MinimumPeriodicInterval &&
                           options.RegistryCleanupInterval <=
                           BackgroundTaskTimerLimits.MaximumTimerDuration,
                $"Registry cleanup interval must be between " +
                $"{BackgroundTaskTimerLimits.MinimumPeriodicInterval} and " +
                $"{BackgroundTaskTimerLimits.MaximumTimerDuration}.")
            .Validate(
                options => options.MaxActiveTasks > 0 &&
                           options.MaxCompletedTaskHistory > 0,
                "Active task and completed task history limits must be greater than zero.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IBackgroundTaskQueue>(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<BackgroundTaskEngineOptions>>()
                .Value;
            var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

            return new InMemoryBackgroundTaskQueue(options, timeProvider);
        });

        services.AddSingleton<BackgroundTaskDispatcher>();
        services.AddSingleton<BackgroundTaskMetrics>();
        services.AddSingleton<BackgroundTaskRescheduler>();
        services.AddSingleton<BackgroundTaskConcurrencyGate>();

        services.AddSingleton<BackgroundTaskEngine>();
        services.AddSingleton<IBackgroundTaskEngine>(serviceProvider =>
            serviceProvider.GetRequiredService<BackgroundTaskEngine>());
        services.AddSingleton<IBackgroundTaskEngineLifecycle>(serviceProvider =>
            serviceProvider.GetRequiredService<BackgroundTaskEngine>());

        services.AddSingleton<BackgroundTaskAttemptRunner>();
        services.AddSingleton<BackgroundTaskWorker>();

        services.AddSingleton<PeriodicBackgroundTaskScheduler>();
        services.AddSingleton<IBackgroundTaskScheduler>(serviceProvider =>
            serviceProvider.GetRequiredService<PeriodicBackgroundTaskScheduler>());

        services.AddHostedService<BackgroundTaskHostedService>();
        services.AddSingleton<BackgroundTaskRuntimeState>();

        services
            .AddHealthChecks()
            .AddCheck<BackgroundTaskHealthCheck>(
                "background-tasks",
                tags: ["ready"]);

        return services;
    }

    private static bool IsValidWorkerCount(int workerCount)
    {
        return workerCount is > 0 and <=
            BackgroundTaskEngineOptions.MaximumWorkersPerQueue;
    }

    private static long GetTotalWorkerCount(BackgroundTaskEngineOptions options)
    {
        return (long)options.InteractiveWorkerCount +
               options.SynchronizationWorkerCount +
               options.MaintenanceWorkerCount;
    }
}
