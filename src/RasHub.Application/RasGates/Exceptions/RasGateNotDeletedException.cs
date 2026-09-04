namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasGateNotDeletedException(Guid rasGateId)
    : Exception($"RasGate '{rasGateId}' is not deleted.");
