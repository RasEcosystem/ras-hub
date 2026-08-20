using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Infobases;

public sealed class SynchronizeInfobaseTask(
    Guid rasGateId,
    Guid clusterId,
    Guid infobaseId,
    string? clusterUser = null,
    string? clusterPassword = null)
    : IBackgroundTask
{
    public Guid RasGateId { get; } = rasGateId;

    public Guid ClusterId { get; } = clusterId;

    public Guid InfobaseId { get; } = infobaseId;

    public string? ClusterUser { get; } = clusterUser;

    public string? ClusterPassword { get; } = clusterPassword;

    public override string ToString()
    {
        return nameof(SynchronizeInfobaseTask);
    }
}