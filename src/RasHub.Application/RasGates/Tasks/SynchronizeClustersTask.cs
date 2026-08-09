using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks;

public sealed record SynchronizeClustersTask(Guid RasGateId)
    : IBackgroundTask;