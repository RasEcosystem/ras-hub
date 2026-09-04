using RasHub.Domain;

namespace RasHub.Application.RasEndpoints.Models;

public sealed record RasEndpointExecutionTarget(
    RasEndpoint Endpoint,
    RasGate Gate)
{
    public RasEndpointAddress Address => RasEndpointAddress.Create(
        Endpoint.Host,
        Endpoint.Port);

    public RasEndpointExecutionGuard CaptureGuard()
    {
        return new RasEndpointExecutionGuard(
            Endpoint.Id,
            Endpoint.ConfigurationRevision,
            Gate.Id,
            Gate.ConfigurationRevision);
    }
}
