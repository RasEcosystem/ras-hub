using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks;

public sealed record SynchronizeClusterTask(
    Guid RasGateId,
    Guid ClusterId)
    : IBackgroundTask;