namespace RasHub.Application.RasEndpoints.Exceptions;

public sealed class RasEndpointNotDeletedException(Guid rasEndpointId)
    : Exception($"RAS endpoint '{rasEndpointId}' is not deleted.");
