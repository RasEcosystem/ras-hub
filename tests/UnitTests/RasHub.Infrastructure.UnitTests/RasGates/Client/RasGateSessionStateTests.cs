using RasHub.Infrastructure.RasGates.Client;

namespace RasHub.Infrastructure.UnitTests.RasGates.Client;

public sealed class RasGateSessionStateTests
{
    [Fact]
    public void ToString_state_contains_api_key_redacts_secret()
    {
        const string apiKey = "session-state-secret";
        var state = new RasGateSessionState(
            new Uri("https://gate.example.test/"),
            apiKey,
            Guid.NewGuid(),
            7);

        var value = state.ToString();

        Assert.DoesNotContain(apiKey, value);
        Assert.Contains("[REDACTED]", value);
    }
}
