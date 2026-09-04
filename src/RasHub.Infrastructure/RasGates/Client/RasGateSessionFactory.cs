using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Domain;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;

namespace RasHub.Infrastructure.RasGates.Client;

internal sealed class RasGateSessionFactory(
    HttpClient httpClient,
    IRasGateEndpointFactory endpointFactory,
    RacVersionCache racVersionCache,
    RacVersionParser versionParser,
    RacCapabilityResolver capabilityResolver)
{
    public RasGateSession Create(RasGate rasGate)
    {
        if (!rasGate.IsActive)
            throw new RasGateInactiveException(rasGate.Id);

        return new RasGateSession(
            httpClient,
            new RasGateSessionState(
                CreateBaseAddress(rasGate),
                rasGate.ApiKey,
                rasGate.Id,
                rasGate.ConfigurationRevision),
            racVersionCache,
            versionParser,
            capabilityResolver);
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
