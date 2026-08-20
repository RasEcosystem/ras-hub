using RasHub.Contracts.RasHub.Requests;

namespace RasHub.Contracts.UnitTests.RasHub.Requests.Gates;

public sealed class RasGateRequestSecurityTests
{
    [Fact]
    public void Create_ToString_with_api_key_does_not_expose_value()
    {
        var request = new CreateRasGateRequest(
            "Local Gate",
            "https://gate.example.test",
            8443,
            "gate-secret");

        Assert.Equal(nameof(CreateRasGateRequest), request.ToString());
    }

    [Fact]
    public void Update_ToString_with_api_key_does_not_expose_value()
    {
        var request = new UpdateRasGateRequest(
            "Local Gate",
            "https://gate.example.test",
            8443,
            true,
            "gate-secret");

        Assert.Equal(nameof(UpdateRasGateRequest), request.ToString());
    }
}