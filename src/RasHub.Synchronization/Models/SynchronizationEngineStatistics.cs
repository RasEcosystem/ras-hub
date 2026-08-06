namespace RasHub.Synchronization;

public sealed record SynchronizationEngineStatistics(
    int TrackedTasks,
    int InteractiveQueueLength,
    int SynchronizationQueueLength,
    int MaintenanceQueueLength);