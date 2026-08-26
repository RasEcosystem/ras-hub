using RasHub.Domain;

namespace RasHub.Infrastructure.IntegrationTests.Database;

internal static class RasGateTestData
{
    public static RasGate Create(
        string name = "Gate",
        string url = "https://gate.example.test",
        int port = 443,
        string apiKey = "secret")
    {
        return new RasGate { Name = name, Url = url, Port = port, ApiKey = apiKey };
    }
}
