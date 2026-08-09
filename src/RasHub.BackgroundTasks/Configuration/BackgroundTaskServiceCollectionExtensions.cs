using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Internal.Diagnostics;
using RasHub.BackgroundTasks.Internal.Engine;
using RasHub.BackgroundTasks.Internal.Execution;
using RasHub.BackgroundTasks.Internal.Processing;
using RasHub.BackgroundTasks.Internal.Queues;
using RasHub.BackgroundTasks.Internal.Recovery;
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
                options => options.SynchronizationQueueCapacity > 0,
                "Synchronization queue capacity must be greater than zero.")
            .Validate(
                options => options.SynchronizationWorkerCount > 0,
                "Synchronization worker count must be greater than zero.")
            .Validate(
                options => options.InteractiveQueueCapacity > 0 &&
                           options.MaintenanceQueueCapacity > 0,
                "All background task queue capacities must be greater than zero.")
            .Validate(
                options => options.InteractiveWorkerCount > 0 &&
                           options.MaintenanceWorkerCount > 0,
                "All background task worker counts must be greater than zero.")
            .Validate(
                options => options.PriorityFairnessInterval > 0,
                "Priority fairness interval must be greater than zero.")
            .Validate(
                options => options.CompletedTaskRetention >= TimeSpan.Zero &&
                           options.RegistryCleanupInterval > TimeSpan.Zero,
                "Task retention cannot be negative and cleanup interval must be positive.")
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

            return new InMemoryBackgroundTaskQueue(options);
        });

        services.AddSingleton<BackgroundTaskDispatcher>();
        services.AddSingleton<BackgroundTaskMetrics>();
        services.AddSingleton<BackgroundTaskRescheduler>();
        services.AddSingleton<BackgroundTaskConcurrencyGate>();
        services.AddSingleton<BackgroundTaskRecoveryRunner>();
        services.AddSingleton<BackgroundTaskWorker>();

        services.AddSingleton<BackgroundTaskEngine>();
        services.AddSingleton<IBackgroundTaskEngine>(serviceProvider =>
            serviceProvider.GetRequiredService<BackgroundTaskEngine>());

        services.AddSingleton<PeriodicBackgroundTaskScheduler>();
        services.AddSingleton<IBackgroundTaskScheduler>(serviceProvider =>
            serviceProvider.GetRequiredService<PeriodicBackgroundTaskScheduler>());

        services.AddHostedService<BackgroundTaskHostedService>();

        services
            .AddHealthChecks()
            .AddCheck<BackgroundTaskHealthCheck>(
                "background-tasks",
                tags: ["ready"]);

        return services;
    }
}