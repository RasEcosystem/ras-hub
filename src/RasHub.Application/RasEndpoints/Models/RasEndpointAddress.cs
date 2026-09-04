using System.Net;
using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Domain;

namespace RasHub.Application.RasEndpoints.Models;

public sealed record RasEndpointAddress
{
    private RasEndpointAddress(string host, int port)
    {
        Host = host;
        Port = port;
    }

    public string Host { get; }

    public int Port { get; }

    public static RasEndpointAddress Create(string host, int port)
    {
        if (port is < 1 or > 65_535)
            throw new RasEndpointAddressValidationException();

        if (string.IsNullOrWhiteSpace(host))
            throw new RasEndpointAddressValidationException();

        var candidate = host.Trim();
        if (candidate.Length >= 2 &&
            candidate[0] == '[' &&
            candidate[^1] == ']')
            candidate = candidate[1..^1];

        string normalizedHost;
        if (IPAddress.TryParse(candidate, out var ipAddress))
        {
            normalizedHost = ipAddress.ToString();
        }
        else
        {
            candidate = candidate.TrimEnd('.');
            if (Uri.CheckHostName(candidate) != UriHostNameType.Dns)
                throw new RasEndpointAddressValidationException();

            normalizedHost = candidate.ToLowerInvariant();
        }

        if (normalizedHost.Length is 0 or > RasEndpoint.HostMaxLength)
            throw new RasEndpointAddressValidationException();

        return new RasEndpointAddress(normalizedHost, port);
    }

    public override string ToString()
    {
        return Host.Contains(':', StringComparison.Ordinal)
            ? $"[{Host}]:{Port}"
            : $"{Host}:{Port}";
    }
}
