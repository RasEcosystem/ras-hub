using System.Text.Json;
using RasHub.Contracts.RasHub.Models;

namespace RasHub.Contracts.UnitTests.RasHub.Models;

public sealed class ResourceModelSerializationTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void ClusterModel_json_round_trip_preserves_value()
    {
        var model = new ClusterModel(
            Guid.Parse("b27fa2da-76fe-45db-a96a-8dc08792c883"),
            "Production cluster",
            "cluster.example.test",
            1541,
            60,
            0,
            1_048_576,
            300,
            1,
            2,
            ClusterLoadBalancingMode.Performance,
            5,
            true,
            false,
            true,
            10,
            30,
            "0 3 * * *",
            new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(model, SerializerOptions);
        var result = JsonSerializer.Deserialize<ClusterModel>(
            json,
            SerializerOptions);

        Assert.Equal(model, result);
    }

    [Fact]
    public void InfobaseModel_json_round_trip_preserves_value()
    {
        var model = new InfobaseModel(
            Guid.Parse("4977ebd0-5689-4a8f-99d5-867b076101ba"),
            "Accounting",
            "Production accounting",
            new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(model, SerializerOptions);
        var result = JsonSerializer.Deserialize<InfobaseModel>(
            json,
            SerializerOptions);

        Assert.Equal(model, result);
    }
}