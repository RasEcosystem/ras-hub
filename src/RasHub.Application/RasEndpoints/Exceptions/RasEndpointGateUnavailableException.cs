using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasEndpoints.Exceptions;

public sealed class RasEndpointGateUnavailableException(
    Guid rasEndpointId,
    Guid rasGateId)
    : NonRetryableBackgroundTaskException(
        $"RAS endpoint '{rasEndpointId}' is assigned to unavailable RasGate '{rasGateId}'.")
{
    public Guid RasEndpointId { get; } = rasEndpointId;

    public Guid RasGateId { get; } = rasGateId;
}
