namespace RasHub.Infrastructure.RasGates;

internal sealed class RasGateHttpClientTransport : IDisposable
{
    public RasGateHttpClientTransport()
    {
        Client = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
    }
}