using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasClusterNotFoundException(
    Guid rasGateId,
    Guid clusterId)
    : NonRetryableBackgroundTaskException(
        $"RasCluster '{clusterId}' was not found for RasGate '{rasGateId}'.")
{
    public Guid RasGateId { get; } = rasGateId;

    public Guid ClusterId { get; } = clusterId;
}