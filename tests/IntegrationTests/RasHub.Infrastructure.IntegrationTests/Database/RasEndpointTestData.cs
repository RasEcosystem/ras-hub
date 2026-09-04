using RasHub.Domain;

namespace RasHub.Infrastructure.IntegrationTests.Database;

internal static class RasEndpointTestData
{
    public static RasEndpoint Create(
        Guid rasGateId,
        string name = "Production RAS",
        string host = "ras.example.test",
        int port = 1545)
    {
        return new RasEndpoint
        {
            Name = name,
            RasGateId = rasGateId,
            Host = host,
            Port = port
        };
    }
}
