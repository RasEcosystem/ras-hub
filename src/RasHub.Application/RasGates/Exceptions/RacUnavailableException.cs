namespace RasHub.Application.RasGates.Exceptions;

public sealed class RacUnavailableException(Guid rasGateId)
    : RasGateClientException(
        $"RAC is unavailable through RasGate '{rasGateId}'.")
{
    public Guid RasGateId { get; } = rasGateId;
}