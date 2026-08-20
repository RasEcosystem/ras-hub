using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;

namespace RasHub.Infrastructure.RasGates.Endpoints;

public sealed class RasGateEndpointFactory : IRasGateEndpointFactory
{
    public Uri CreateBaseAddress(string url, int port)
    {
        if (port is < 1 or > 65_535 ||
            !Uri.TryCreate(url, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttp &&
             address.Scheme != Uri.UriSchemeHttps))
            throw InvalidEndpoint();

        if (!string.IsNullOrEmpty(address.UserInfo) ||
            !string.IsNullOrEmpty(address.Query) ||
            !string.IsNullOrEmpty(address.Fragment) ||
            (!address.IsDefaultPort && address.Port != port))
            throw InvalidEndpoint();

        var builder = new UriBuilder(address) { Port = port };

        if (!builder.Path.EndsWith('/'))
            builder.Path += '/';

        return builder.Uri;
    }

    private static RasGateEndpointValidationException InvalidEndpoint()
    {
        return new RasGateEndpointValidationException(
            "The RasGate endpoint URL is invalid.");
    }
}
