using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed class RemoveClusterTask(
    Guid rasEndpointId,
    Guid clusterId,
    string? clusterUser,
    string? clusterPassword)
    : IBackgroundTask
{
    public Guid RasEndpointId { get; } = rasEndpointId;

    public Guid ClusterId { get; } = clusterId;

    public string? ClusterUser { get; } = clusterUser;

    public string? ClusterPassword { get; } = clusterPassword;

    public override string ToString()
    {
        return nameof(RemoveClusterTask);
    }
}
