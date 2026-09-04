namespace RasHub.Infrastructure.RasGates.Client;

internal sealed class RasGateHttpClientTransport : IDisposable
{
    public RasGateHttpClientTransport()
    {
        Client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
    }
}
