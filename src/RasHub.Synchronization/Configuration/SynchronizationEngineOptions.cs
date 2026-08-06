namespace RasHub.Synchronization;

public sealed class SynchronizationEngineOptions
{
    public const string SectionName = "Synchronization";

    public const int DefaultQueueCapacity = 1_024;
    public const int DefaultWorkerCount = 4;

    public int InteractiveQueueCapacity { get; set; } = 256;

    public int QueueCapacity { get; set; } =
        DefaultQueueCapacity;

    public int MaintenanceQueueCapacity { get; set; } = 256;

    public int InteractiveWorkerCount { get; set; } = 2;

    public int WorkerCount { get; set; } =
        DefaultWorkerCount;

    public int MaintenanceWorkerCount { get; set; } = 1;

    public int PriorityFairnessInterval { get; set; } = 16;

    public TimeSpan CompletedTaskRetention { get; set; } =
        TimeSpan.FromMinutes(10);

    public TimeSpan RegistryCleanupInterval { get; set; } =
        TimeSpan.FromMinutes(1);

    public int MaxTrackedTasks { get; set; } = 10_000;
}