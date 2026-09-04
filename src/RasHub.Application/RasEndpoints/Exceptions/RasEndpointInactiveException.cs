using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasEndpoints.Exceptions;

public sealed class RasEndpointInactiveException(Guid rasEndpointId)
    : NonRetryableBackgroundTaskException(
        $"RAS endpoint '{rasEndpointId}' is inactive.")
{
    public Guid RasEndpointId { get; } = rasEndpointId;
}
