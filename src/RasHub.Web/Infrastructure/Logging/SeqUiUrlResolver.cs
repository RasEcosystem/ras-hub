namespace RasHub.Web.Infrastructure.Logging;

public static class SeqUiUrlResolver
{
    public static string? Resolve(string? configuredUrl, Uri applicationBaseUri)
    {
        if (string.IsNullOrWhiteSpace(configuredUrl))
            return null;

        if (!Uri.TryCreate(configuredUrl.Trim(), UriKind.RelativeOrAbsolute, out var configuredUri))
            return null;

        if (!configuredUri.IsAbsoluteUri)
            configuredUri = new Uri(applicationBaseUri, configuredUri);

        if (!configuredUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !configuredUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return null;

        if (IsEnvironmentLocalHost(configuredUri.Host) &&
            !HostsMatch(configuredUri, applicationBaseUri))
            configuredUri = new UriBuilder(configuredUri)
            {
                Host = applicationBaseUri.Host
            }.Uri;

        return configuredUri.AbsoluteUri;
    }

    private static bool IsEnvironmentLocalHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("seq", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HostsMatch(Uri left, Uri right)
    {
        return left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase) ||
               (left.IsLoopback && right.IsLoopback);
    }
}