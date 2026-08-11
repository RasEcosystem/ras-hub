using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters;

namespace RasHub.Infrastructure.RasGates.Client;

internal sealed class RasGateClientFactory(
    RasGateHttpClientTransport transport,
    IRasGateEndpointFactory endpointFactory,
    RacVersionCache racVersionCache,
    RacVersionParser versionParser,
    RacCapabilityResolver capabilityResolver,
    RacResourceAdapterResolver<RasClusterSnapshot> clusterAdapterResolver,
    RacResultCommandAdapterResolver<RasClusterCreationOptions, Guid>
        clusterInsertAdapterResolver,
    RacCommandAdapterResolver<UpdateRasClusterCommand>
        clusterUpdateAdapterResolver,
    RacCommandAdapterResolver<RemoveRasClusterCommand>
        clusterRemoveAdapterResolver)
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
            rasGate.Id,
            rasGate.ConfigurationRevision,
            racVersionCache,
            versionParser,
            capabilityResolver,
            clusterAdapterResolver,
            clusterInsertAdapterResolver,
            clusterUpdateAdapterResolver,
            clusterRemoveAdapterResolver);
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