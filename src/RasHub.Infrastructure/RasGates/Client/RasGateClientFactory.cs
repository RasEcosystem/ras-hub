using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;

namespace RasHub.Infrastructure.RasGates.Client;

internal sealed class RasGateClientFactory(
    RasGateHttpClientTransport transport,
    IRasGateEndpointFactory endpointFactory,
    RacVersionParser versionParser,
    RacCapabilityResolver capabilityResolver,
    RacResourceAdapterResolver<RasClusterSnapshot> clusterAdapterResolver)
    : IRasGateClientFactory
{
    public IRasGateClient Create(RasGate rasGate)
    {
        if (!rasGate.IsActive)
            throw new RasGateInactiveException(rasGate.Id);

        return new HttpRasGateClient(
            transport.Client,
            CreateBaseAddress(rasGate),
            rasGate.ApiKey,
            versionParser,
            capabilityResolver,
            clusterAdapterResolver);
    }

    private Uri CreateBaseAddress(RasGate rasGate)
    {
        try
        {
            return endpointFactory.CreateBaseAddress(rasGate.Url, rasGate.Port);
        }
        catch (RasGateEndpointValidationException exception)
        {
            throw new RasGateClientException(
                $"RasGate '{rasGate.Id}' has an invalid endpoint.",
                exception);
        }
    }
}