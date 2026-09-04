namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasGateRevisionConflictException(Guid rasGateId)
    : Exception($"RasGate '{rasGateId}' changed after it was read.")
{
    public Guid RasGateId { get; } = rasGateId;
}
