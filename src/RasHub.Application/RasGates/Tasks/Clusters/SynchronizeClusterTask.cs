using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed record SynchronizeClusterTask(
    Guid RasEndpointId,
    Guid ClusterId)
    : IBackgroundTask;
