using RasHub.Application.RasGates.Models;

namespace RasHub.Infrastructure.UnitTests.RasGates.Models;

public sealed class RasGateRegistrationTests
{
    [Fact]
    public void ToString_registration_contains_api_key_does_not_disclose_secret()
    {
        const string apiKey = "registration-secret";
        var registration = new RasGateRegistration(
            "Gate",
            "https://gate.example.test",
            443,
            apiKey,
            true);

        Assert.DoesNotContain(apiKey, registration.ToString());
    }

    [Fact]
    public void ToString_update_contains_api_key_does_not_disclose_secret()
    {
        const string apiKey = "registration-update-secret";
        var update = new RasGateRegistrationUpdate(
            "Gate",
            "https://gate.example.test",
            443,
            true,
            1,
            apiKey);

        Assert.DoesNotContain(apiKey, update.ToString());
    }
}
