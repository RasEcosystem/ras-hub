namespace RasHub.Synchronization.Configuration;

/// <summary>
///     Configures queue capacities, worker quotas, fairness, and completed-task retention.
/// </summary>
public sealed class SynchronizationEngineOptions
{
    public const string SectionName = "Synchronization";

    public const int DefaultQueueCapacity = 1_024;
    public const int DefaultWorkerCount = 4;

    /// <summary>Maximum number of waiting interactive tasks.</summary>
    public int InteractiveQueueCapacity { get; set; } = 256;

    /// <summary>Maximum number of waiting synchronization tasks.</summary>
    public int QueueCapacity { get; set; } =
        DefaultQueueCapacity;

    /// <summary>Maximum number of waiting maintenance tasks.</summary>
    public int MaintenanceQueueCapacity { get; set; } = 256;

    /// <summary>Workers reserved for user-facing interactive work.</summary>
    public int InteractiveWorkerCount { get; set; } = 2;

    /// <summary>Workers reserved for regular synchronization work.</summary>
    public int WorkerCount { get; set; } =
        DefaultWorkerCount;

    /// <summary>Workers reserved for low-priority maintenance work.</summary>
    public int MaintenanceWorkerCount { get; set; } = 1;

    /// <summary>How often a lane selects its oldest task instead of its highest-priority task.</summary>
    public int PriorityFairnessInterval { get; set; } = 16;

    /// <summary>How long terminal task snapshots remain queryable.</summary>
    public TimeSpan CompletedTaskRetention { get; set; } =
        TimeSpan.FromMinutes(10);

    /// <summary>How often expired terminal task snapshots are removed.</summary>
    public TimeSpan RegistryCleanupInterval { get; set; } =
        TimeSpan.FromMinutes(1);

    /// <summary>Maximum number of pending, running, delayed, and retained executions tracked in memory.</summary>
    public int MaxTrackedTasks { get; set; } = 10_000;
}