using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasClusterNotFoundException(
    Guid rasEndpointId,
    Guid clusterId)
    : NonRetryableBackgroundTaskException(
        $"RasCluster '{clusterId}' was not found for RAS endpoint " +
        $"'{rasEndpointId}'.")
{
    public Guid RasEndpointId { get; } = rasEndpointId;

    public Guid ClusterId { get; } = clusterId;
}
