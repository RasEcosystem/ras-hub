using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasGateConfigurationChangedException(Guid rasGateId)
    : NonRetryableBackgroundTaskException(
        $"RasGate '{rasGateId}' changed while synchronization was in progress.")
{
    public Guid RasGateId { get; } = rasGateId;
}