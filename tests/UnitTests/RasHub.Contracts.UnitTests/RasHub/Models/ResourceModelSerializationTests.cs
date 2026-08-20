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
        var model = new ClusterModel
        {
            Id = Guid.Parse("b27fa2da-76fe-45db-a96a-8dc08792c883"),
            Name = "Production cluster",
            Host = "cluster.example.test",
            Port = 1541,
            ExpirationTimeoutSeconds = 60,
            LifetimeLimitSeconds = 0,
            MaxMemorySizeKb = 1_048_576,
            MaxMemoryTimeLimitSeconds = 300,
            SecurityLevel = 1,
            SessionFaultToleranceLevel = 2,
            LoadBalancingMode = ClusterLoadBalancingMode.Performance,
            ErrorsCountThresholdPercent = 5,
            KillProblemProcesses = true,
            KillByMemoryWithDump = false,
            AllowAccessRightAuditEventsRecording = true,
            PingPeriod = 10,
            PingTimeout = 30,
            RestartSchedule = "0 3 * * *",
            ObservedAt = new DateTime(
                2026,
                8,
                20,
                12,
                0,
                0,
                DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(model, SerializerOptions);
        var result = JsonSerializer.Deserialize<ClusterModel>(
            json,
            SerializerOptions);

        Assert.Equal(model, result);
    }
}
