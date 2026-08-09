using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Domain;
using RasHub.Infrastructure.RasGates.Serialization;

namespace RasHub.Infrastructure.RasGates;

internal sealed class RasGateClientFactory(
    RasGateHttpClientTransport transport,
    RacClusterOutputDeserializer clusterDeserializer)
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
            clusterDeserializer);
    }

    private static Uri CreateBaseAddress(RasGate rasGate)
    {
        if (!Uri.TryCreate(rasGate.Url, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttp &&
             address.Scheme != Uri.UriSchemeHttps))
            throw new RasGateClientException(
                $"RasGate '{rasGate.Id}' has an invalid URL.");

        var builder = new UriBuilder(address)
        {
            Port = rasGate.Port
        };

        if (!builder.Path.EndsWith('/'))
            builder.Path += '/';

        return builder.Uri;
    }
}