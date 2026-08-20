using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Deserialization;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Clusters;

public sealed class RacClusterInfoV1AdapterTests
{
    private static readonly Guid ClusterId =
        Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7");

    private readonly RacClusterInfoV1Adapter _adapter = new(
        new RacClusterOutputDeserializerResolver(
        [
            new RacClusterOutputV1Deserializer(
                new RacKeyValueOutputDeserializer())
        ]));

    [Fact]
    public void Create_command_includes_requested_cluster_id()
    {
        Assert.Equal(
            ["cluster", "info", $"--cluster={ClusterId:D}"],
            _adapter.CreateCommand(ClusterId));
    }

    [Fact]
    public void Parse_single_matching_cluster_returns_complete_snapshot()
    {
        var snapshot = _adapter.Parse(
            new Version(8, 3, 27, 2214),
            SuccessfulExecution(CreateClusterOutput(ClusterId)),
            ClusterId);

        var cluster = Assert.Single(snapshot.Items);
        Assert.Equal(SnapshotCompleteness.Complete, snapshot.Completeness);
        Assert.Equal(ClusterId, cluster.ExternalId);
        Assert.Equal("RasCluster", cluster.Name);
    }

    [Fact]
    public void Parse_cluster_with_different_id_rejects_result()
    {
        Assert.Throws<RasGateClientException>(() => _adapter.Parse(
            new Version(8, 3, 27, 2214),
            SuccessfulExecution(CreateClusterOutput(Guid.NewGuid())),
            ClusterId));
    }

    [Fact]
    public void Parse_empty_successful_output_reports_missing_cluster()
    {
        var exception = Assert.Throws<RacResourceNotFoundException>(() =>
            _adapter.Parse(
                new Version(8, 3, 27, 2214),
                SuccessfulExecution(string.Empty),
                ClusterId));

        Assert.Equal("clusters", exception.Resource);
        Assert.Equal(ClusterId, exception.ExternalId);
    }

    [Fact]
    public void Parse_malformed_output_rejects_result()
    {
        Assert.Throws<RacOutputDeserializationException>(() => _adapter.Parse(
            new Version(8, 3, 27, 2214),
            SuccessfulExecution("not a key-value record"),
            ClusterId));
    }

    [Fact]
    public void MinimumVersion_V1_adapter_returns_baseline_version()
    {
        Assert.Equal(new Version(8, 3, 27, 2214), _adapter.MinimumVersion);
    }

    [Fact]
    public void Parse_version_above_previous_family_boundary_accepts_snapshot()
    {
        var snapshot = _adapter.Parse(
            new Version(8, 4, 0, 0),
            SuccessfulExecution(CreateClusterOutput(ClusterId)),
            ClusterId);

        Assert.Equal(SnapshotCompleteness.Complete, snapshot.Completeness);
    }

    [Fact]
    public void Parse_version_below_minimum_rejects_snapshot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _adapter.Parse(
            new Version(8, 3, 27, 2213),
            SuccessfulExecution(CreateClusterOutput(ClusterId)),
            ClusterId));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(-1, true)]
    public void Parse_failed_execution_rejects_result(int exitCode, bool timedOut)
    {
        var execution = SuccessfulExecution(string.Empty) with
        {
            Outcome = timedOut
                ? RacExecutionOutcome.Unknown
                : RacExecutionOutcome.Failed,
            ExitCode = exitCode,
            TimedOut = timedOut
        };

        Assert.Throws<RasGateClientException>(() => _adapter.Parse(
            new Version(8, 3, 27, 2214),
            execution,
            ClusterId));
    }

    [Fact]
    public void Parse_failed_execution_does_not_expose_RAC_error_output()
    {
        var execution = SuccessfulExecution(string.Empty) with
        {
            Outcome = RacExecutionOutcome.Failed,
            ExitCode = -1,
            StandardError = "Cluster was not found."
        };

        var exception = Assert.Throws<RasGateClientException>(() =>
            _adapter.Parse(
                new Version(8, 3, 27, 2214),
                execution,
                ClusterId));

        Assert.Equal(
            "RAC cluster info command failed with exit code -1.",
            exception.Message);
        Assert.DoesNotContain("Cluster was not found.", exception.Message);
    }

    [Fact]
    public void Create_command_without_cluster_id_rejects_request()
    {
        Assert.Throws<ArgumentException>(() => _adapter.CreateCommand());
    }

    private static RacExecutionResult SuccessfulExecution(string output)
    {
        return new RacExecutionResult
        {
            Outcome = RacExecutionOutcome.Succeeded,
            ExitCode = 0,
            StandardOutput = output,
            StandardError = string.Empty,
            DurationMilliseconds = 1,
            TimedOut = false
        };
    }

    private static string CreateClusterOutput(Guid clusterId)
    {
        return
            $"cluster : {clusterId:D}\n" +
            "host : localhost\n" +
            "port : 15455\n" +
            "name : \"RasCluster\"\n" +
            "expiration-timeout : 60\n" +
            "lifetime-limit : 0\n" +
            "max-memory-size : 0\n" +
            "max-memory-time-limit : 0\n" +
            "security-level : 0\n" +
            "session-fault-tolerance-level : 0\n" +
            "load-balancing-mode : performance\n" +
            "errors-count-threshold : 0\n" +
            "kill-problem-processes : 1\n" +
            "kill-by-memory-with-dump : 0\n" +
            "allow-access-right-audit-events-recording : 0\n" +
            "ping-period : 0\n" +
            "ping-timeout : 0\n" +
            "restart-schedule : \n";
    }
}
