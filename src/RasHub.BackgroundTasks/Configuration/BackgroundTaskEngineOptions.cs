namespace RasHub.BackgroundTasks.Configuration;

/// <summary>
///     Configures queue capacities, worker quotas, and completed-task retention.
/// </summary>
public sealed class BackgroundTaskEngineOptions
{
    public const string SectionName = "BackgroundTasks";

    public const int MaximumWorkersPerQueue = 1_024;
    public const int MaximumTotalWorkerCount = 2_048;

    public const int DefaultSynchronizationQueueCapacity = 1_024;
    public const int DefaultSynchronizationWorkerCount = 16;

    /// <summary>External admission capacity for waiting interactive tasks.</summary>
    public int InteractiveQueueCapacity { get; set; } = 256;

    /// <summary>External admission capacity for waiting synchronization tasks.</summary>
    public int SynchronizationQueueCapacity { get; set; } =
        DefaultSynchronizationQueueCapacity;

    /// <summary>External admission capacity for waiting maintenance tasks.</summary>
    public int MaintenanceQueueCapacity { get; set; } = 256;

    /// <summary>Workers reserved for user-facing interactive work.</summary>
    public int InteractiveWorkerCount { get; set; } = 8;

    /// <summary>Workers reserved for regular synchronization work.</summary>
    public int SynchronizationWorkerCount { get; set; } =
        DefaultSynchronizationWorkerCount;

    /// <summary>Workers reserved for low-priority maintenance work.</summary>
    public int MaintenanceWorkerCount { get; set; } = 2;

    /// <summary>How long terminal task snapshots remain queryable.</summary>
    public TimeSpan CompletedTaskRetention { get; set; } =
        TimeSpan.FromMinutes(10);

    /// <summary>How often expired terminal task snapshots are removed.</summary>
    public TimeSpan RegistryCleanupInterval { get; set; } =
        TimeSpan.FromMinutes(1);

    /// <summary>Maximum number of pending, running, and delayed executions.</summary>
    public int MaxActiveTasks { get; set; } = 10_000;

    /// <summary>Maximum number of terminal execution snapshots retained for observation.</summary>
    public int MaxCompletedTaskHistory { get; set; } = 1_000;
}
