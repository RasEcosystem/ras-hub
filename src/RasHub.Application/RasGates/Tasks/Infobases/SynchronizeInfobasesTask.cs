using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Infobases;

public sealed class SynchronizeInfobasesTask(
    Guid rasEndpointId,
    Guid clusterId,
    string? clusterUser = null,
    string? clusterPassword = null)
    : IBackgroundTask<CollectionSynchronizationResult>
{
    public Guid RasEndpointId { get; } = rasEndpointId;

    public Guid ClusterId { get; } = clusterId;

    public string? ClusterUser { get; } = clusterUser;

    public string? ClusterPassword { get; } = clusterPassword;

    public override string ToString()
    {
        return nameof(SynchronizeInfobasesTask);
    }
}
