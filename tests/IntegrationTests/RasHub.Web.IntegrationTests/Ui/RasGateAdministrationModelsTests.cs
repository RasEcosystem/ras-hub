using RasHub.Web.Infrastructure.RasGates;

namespace RasHub.Web.IntegrationTests.Ui;

public sealed class RasGateAdministrationModelsTests
{
    [Fact]
    public void Editor_values_do_not_disclose_api_key_in_string_representation()
    {
        const string apiKey = "ras-gate-api-key-secret";
        var values = new RasGateEditorValues(
            "RasGate",
            "https://gate.example.test",
            443,
            apiKey,
            true);

        Assert.DoesNotContain(apiKey, values.ToString());
    }
}
