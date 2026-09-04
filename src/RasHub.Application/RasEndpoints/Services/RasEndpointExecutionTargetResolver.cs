using RasHub.Application.Interfaces;
using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Domain;

namespace RasHub.Application.RasEndpoints.Services;

public sealed class RasEndpointExecutionTargetResolver(
    IRepository<RasEndpoint> endpointRepository,
    IRepository<RasGate> gateRepository)
{
    public async Task<RasEndpointExecutionTarget> ResolveAsync(
        Guid rasEndpointId,
        CancellationToken cancellationToken)
    {
        var endpoint = await endpointRepository.GetByIdAsync(
            rasEndpointId,
            cancellationToken);

        if (endpoint is null)
            throw new RasEndpointNotFoundException(rasEndpointId);
        if (!endpoint.IsActive)
            throw new RasEndpointInactiveException(rasEndpointId);

        var gate = await gateRepository.GetByIdAsync(
            endpoint.RasGateId,
            cancellationToken);

        if (gate is null || !gate.IsActive)
            throw new RasEndpointGateUnavailableException(
                rasEndpointId,
                endpoint.RasGateId);

        return new RasEndpointExecutionTarget(endpoint, gate);
    }
}
