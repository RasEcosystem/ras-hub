namespace RasHub.Application.RasEndpoints.Models;

public sealed record RasEndpointExecutionGuard(
    Guid RasEndpointId,
    long RasEndpointConfigurationRevision,
    Guid RasGateId,
    long RasGateConfigurationRevision);
