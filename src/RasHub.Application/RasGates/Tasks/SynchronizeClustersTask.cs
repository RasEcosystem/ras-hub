using RasHub.Synchronization.Abstractions;

namespace RasHub.Application.RasGates.Tasks;

public sealed record SynchronizeClustersTask(Guid RasGateId)
    : IBackgroundTask;