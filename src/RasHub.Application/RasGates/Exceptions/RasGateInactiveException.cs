using RasHub.Synchronization.Exceptions;

namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasGateInactiveException(Guid rasGateId)
    : NonRetryableBackgroundTaskException($"RasGate '{rasGateId}' is inactive.")
{
    public Guid RasGateId { get; } = rasGateId;
}