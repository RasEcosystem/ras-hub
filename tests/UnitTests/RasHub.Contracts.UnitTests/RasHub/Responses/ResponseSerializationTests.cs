using System.Text.Json;
using System.Text.Json.Serialization;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Responses;

namespace RasHub.Contracts.UnitTests.RasHub.Responses;

public sealed class ResponseSerializationTests
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    [Fact]
    public void RasGateStatusResponse_json_round_trip_preserves_aggregate_snapshot()
    {
        var model = new RasGateStatusResponse
        {
            State = RasGateHealthState.Degraded,
            InstanceName = "RasGate Application",
            RasGateVersion = "0.2.1.0+65b339eaa0",
            RasGateObservedAt = new DateTime(
                2026,
                8,
                20,
                18,
                27,
                19,
                DateTimeKind.Utc),
            RacAvailable = false,
            RacVersion = null,
            RacObservedAt = new DateTime(
                2026,
                8,
                20,
                18,
                27,
                20,
                DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(model, SerializerOptions);
        var result = JsonSerializer.Deserialize<RasGateStatusResponse>(
            json,
            SerializerOptions);

        Assert.Contains("\"state\":\"Degraded\"", json);
        Assert.Equal(model, result);
    }

    [Fact]
    public void RasHubInfoResponse_missing_version_is_rejected()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RasHubInfoResponse>(
                "{}",
                SerializerOptions));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(null, false));
        return options;
    }
}