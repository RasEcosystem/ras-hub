using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasEndpoints.Exceptions;

public sealed class RasEndpointConfigurationChangedException(
    Guid rasEndpointId)
    : NonRetryableBackgroundTaskException(
        $"RAS endpoint '{rasEndpointId}' or its assigned RasGate changed " +
        "while the operation was in progress.")
{
    public Guid RasEndpointId { get; } = rasEndpointId;
}
