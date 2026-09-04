using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasEndpoints.Exceptions;

public sealed class RasEndpointNotFoundException(Guid rasEndpointId)
    : NonRetryableBackgroundTaskException(
        $"RAS endpoint '{rasEndpointId}' was not found.")
{
    public Guid RasEndpointId { get; } = rasEndpointId;
}
