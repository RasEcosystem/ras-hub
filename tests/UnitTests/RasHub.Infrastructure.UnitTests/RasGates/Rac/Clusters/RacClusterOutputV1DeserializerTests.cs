using RasHub.Domain.Enums;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Deserialization;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Clusters;

public sealed class RacClusterOutputV1DeserializerTests
{
    private readonly RacClusterOutputV1Deserializer _deserializer = new(
        new RacKeyValueOutputDeserializer());

    [Fact]
    public void Deserialize_maps_cluster_fields_to_strong_types()
    {
        const string output =
            "cluster : 820d1955-349e-4173-9092-a3f206d328f7\r\n" +
            "host : WIN-P4BDRRBVMU8\r\n" +
            "port : 1541\r\n" +
            "name : \"Локальный кластер\"\r\n" +
            "expiration-timeout : 60\r\n" +
            "lifetime-limit : 0\r\n" +
            "max-memory-size : 0\r\n" +
            "max-memory-time-limit : 0\r\n" +
            "security-level : 0\r\n" +
            "session-fault-tolerance-level : 0\r\n" +
            "load-balancing-mode : performance\r\n" +
            "errors-count-threshold : 0\r\n" +
            "kill-problem-processes : 1\r\n" +
            "kill-by-memory-with-dump : 1\r\n" +
            "allow-access-right-audit-events-recording : 1\r\n" +
            "ping-period : 5\r\n" +
            "ping-timeout : 15\r\n" +
            "restart-schedule : \"0 3 * * *\"\r\n\r\n";

        var cluster = Assert.Single(_deserializer.Deserialize(output));

        Assert.Equal(
            Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7"),
            cluster.ExternalId);
        Assert.Equal("Локальный кластер", cluster.Name);
        Assert.Equal("WIN-P4BDRRBVMU8", cluster.Host);
        Assert.Equal(1541, cluster.Port);
        Assert.Equal(60, cluster.ExpirationTimeoutSeconds);
        Assert.Equal(RasClusterLoadBalancingMode.Performance, cluster.LoadBalancingMode);
        Assert.True(cluster.KillProblemProcesses);
        Assert.True(cluster.KillByMemoryWithDump);
        Assert.True(cluster.AllowAccessRightAuditEventsRecording);
        Assert.Equal(5, cluster.PingPeriod);
        Assert.Equal(15, cluster.PingTimeout);
        Assert.Equal("0 3 * * *", cluster.RestartSchedule);
    }

    [Fact]
    public void Deserialize_marks_fields_absent_in_older_RAC_versions_unknown()
    {
        var cluster = Assert.Single(_deserializer.Deserialize(CreateRequiredOutput()));

        Assert.Null(cluster.KillByMemoryWithDump);
        Assert.Null(cluster.AllowAccessRightAuditEventsRecording);
        Assert.Null(cluster.PingPeriod);
        Assert.Null(cluster.PingTimeout);
        Assert.Null(cluster.RestartSchedule);
    }

    [Theory]
    [InlineData("cluster", "not-a-guid")]
    [InlineData("port", "0")]
    [InlineData("kill-problem-processes", "sometimes")]
    [InlineData("load-balancing-mode", "random")]
    public void Deserialize_rejects_invalid_typed_values(string key, string value)
    {
        var output = CreateRequiredOutput().Replace(
            $"{key} : {GetValidValue(key)}",
            $"{key} : {value}",
            StringComparison.Ordinal);

        Assert.Throws<RacOutputDeserializationException>(() =>
            _deserializer.Deserialize(output));
    }

    private static string CreateRequiredOutput()
    {
        return
            "cluster : 820d1955-349e-4173-9092-a3f206d328f7\n" +
            "host : localhost\n" +
            "port : 1541\n" +
            "name : \"Cluster\"\n" +
            "expiration-timeout : 60\n" +
            "lifetime-limit : 0\n" +
            "max-memory-size : 0\n" +
            "max-memory-time-limit : 0\n" +
            "security-level : 0\n" +
            "session-fault-tolerance-level : 0\n" +
            "load-balancing-mode : performance\n" +
            "errors-count-threshold : 0\n" +
            "kill-problem-processes : 1\n";
    }

    private static string GetValidValue(string key)
    {
        return key switch
        {
            "cluster" => "820d1955-349e-4173-9092-a3f206d328f7",
            "port" => "1541",
            "kill-problem-processes" => "1",
            "load-balancing-mode" => "performance",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
        };
    }
}
