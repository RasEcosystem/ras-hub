using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasGateNotFoundException(Guid rasGateId)
    : NonRetryableBackgroundTaskException(
        $"RasGate '{rasGateId}' was not found.")
{
    public Guid RasGateId { get; } = rasGateId;
}
