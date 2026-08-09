using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasGateCapabilityNotSupportedException(
    Guid rasGateId,
    string resource,
    string operation)
    : NonRetryableBackgroundTaskException(
        $"RasGate '{rasGateId}' does not support '{resource}.{operation}'.")
{
    public Guid RasGateId { get; } = rasGateId;

    public string Resource { get; } = resource;

    public string Operation { get; } = operation;
}