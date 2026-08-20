using System.Text.Json;
using RasHub.Contracts.RasHub.Requests;

namespace RasHub.Contracts.UnitTests.RasHub.Requests;

public sealed class ContractRequestSerializationTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void UpdateRasGateRequest_json_round_trip_preserves_required_activity()
    {
        var request = new UpdateRasGateRequest(
            "Primary gate",
            "https://rasgate.example.test",
            5050,
            false);

        var json = JsonSerializer.Serialize(request, SerializerOptions);
        var result = JsonSerializer.Deserialize<UpdateRasGateRequest>(
            json,
            SerializerOptions);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
        Assert.Null(result.ApiKey);
    }

    [Fact]
    public void UpdateRasGateRequest_json_without_activity_is_rejected()
    {
        const string json =
            """
            {
              "name": "Primary gate",
              "url": "https://rasgate.example.test",
              "port": 5050
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateRasGateRequest>(
                json,
                SerializerOptions));
    }
}