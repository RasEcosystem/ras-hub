using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed record SynchronizeClustersTask(Guid RasGateId)
    : IBackgroundTask<CollectionSynchronizationResult>;
