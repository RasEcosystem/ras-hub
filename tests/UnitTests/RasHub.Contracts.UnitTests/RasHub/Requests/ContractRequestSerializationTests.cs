using System.Text.Json;
using RasHub.Contracts.RasHub.Requests;

namespace RasHub.Contracts.UnitTests.RasHub.Requests;

public sealed class ContractRequestSerializationTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void UpdateRasGateRequest_json_round_trip_preserves_required_fields()
    {
        var request = new UpdateRasGateRequest(
            "Primary gate",
            "https://rasgate.example.test",
            5050,
            false,
            42);

        var json = JsonSerializer.Serialize(request, SerializerOptions);
        var result = JsonSerializer.Deserialize<UpdateRasGateRequest>(
            json,
            SerializerOptions);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
        Assert.Equal(42, result.ExpectedConfigurationRevision);
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

    [Fact]
    public void UpdateRasGateRequest_json_without_expected_revision_is_rejected()
    {
        const string json =
            """
            {
              "name": "Primary gate",
              "url": "https://rasgate.example.test",
              "port": 5050,
              "isActive": true
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateRasGateRequest>(
                json,
                SerializerOptions));
    }

    [Fact]
    public void UpdateRasEndpointRequest_json_round_trip_preserves_required_fields()
    {
        var request = new UpdateRasEndpointRequest(
            "Production RAS",
            Guid.NewGuid(),
            "ras.example.test",
            1545,
            false,
            7);

        var json = JsonSerializer.Serialize(request, SerializerOptions);
        var result = JsonSerializer.Deserialize<UpdateRasEndpointRequest>(
            json,
            SerializerOptions);

        Assert.NotNull(result);
        Assert.Equal(request, result);
    }

    [Theory]
    [InlineData("rasGateId")]
    [InlineData("isActive")]
    [InlineData("expectedConfigurationRevision")]
    public void UpdateRasEndpointRequest_json_without_required_state_is_rejected(
        string propertyToOmit)
    {
        var properties = new Dictionary<string, object?>
        {
            ["name"] = "Production RAS",
            ["rasGateId"] = Guid.NewGuid(),
            ["host"] = "ras.example.test",
            ["port"] = 1545,
            ["isActive"] = true,
            ["expectedConfigurationRevision"] = 7L
        };
        properties.Remove(propertyToOmit);
        var json = JsonSerializer.Serialize(properties, SerializerOptions);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateRasEndpointRequest>(
                json,
                SerializerOptions));
    }

}
