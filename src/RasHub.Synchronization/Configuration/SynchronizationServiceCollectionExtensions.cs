using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Internal.Diagnostics;
using RasHub.Synchronization.Internal.Engine;
using RasHub.Synchronization.Internal.Execution;
using RasHub.Synchronization.Internal.Processing;
using RasHub.Synchronization.Internal.Queues;
using RasHub.Synchronization.Internal.Recovery;
using RasHub.Synchronization.Internal.Scheduling;
using RasHub.Synchronization.Tasks;

namespace RasHub.Synchronization.Configuration;

/// <summary>Registers synchronization services.</summary>
public static class SynchronizationServiceCollectionExtensions
{
    public static IServiceCollection AddRasHubSynchronization(
        this IServiceCollection services,
        Action<SynchronizationEngineOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<SynchronizationEngineOptions>();

        if (configure is not null)
            optionsBuilder.Configure(configure);

        optionsBuilder
            .Validate(
                options => options.QueueCapacity > 0,
                "Synchronization queue capacity must be greater than zero.")
            .Validate(
                options => options.WorkerCount > 0,
                "Synchronization worker count must be greater than zero.")
            .Validate(
                options => options.InteractiveQueueCapacity > 0 &&
                           options.MaintenanceQueueCapacity > 0,
                "All synchronization queue capacities must be greater than zero.")
            .Validate(
                options => options.InteractiveWorkerCount > 0 &&
                           options.MaintenanceWorkerCount > 0,
                "All synchronization worker counts must be greater than zero.")
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
                .GetRequiredService<IOptions<SynchronizationEngineOptions>>()
                .Value;

            return new InMemoryBackgroundTaskQueue(options);
        });

        services.AddSingleton<BackgroundTaskDispatcher>();
        services.AddSingleton<BackgroundTaskMetrics>();
        services.AddSingleton<BackgroundTaskRescheduler>();
        services.AddSingleton<BackgroundTaskConcurrencyGate>();
        services.AddSingleton<BackgroundTaskRecoveryRunner>();
        services.AddSingleton<BackgroundTaskWorker>();
        services.TryAddTransient<
            IBackgroundTaskHandler<TestBackgroundTask>,
            TestBackgroundTaskHandler>();

        services.AddSingleton<SynchronizationEngine>();
        services.AddSingleton<ISynchronizationEngine>(serviceProvider =>
            serviceProvider.GetRequiredService<SynchronizationEngine>());

        services.AddSingleton<PeriodicBackgroundTaskScheduler>();
        services.AddSingleton<IBackgroundTaskScheduler>(serviceProvider =>
            serviceProvider.GetRequiredService<PeriodicBackgroundTaskScheduler>());

        services.AddHostedService<SynchronizationHostedService>();

        services
            .AddHealthChecks()
            .AddCheck<SynchronizationHealthCheck>(
                "synchronization",
                tags: ["ready"]);

        return services;
    }
}