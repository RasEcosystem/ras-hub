using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Deserialization;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Clusters;

public sealed class RacClusterSnapshotV1AdapterTests
{
    private readonly RacClusterSnapshotV1Adapter _adapter = new(
        new RacClusterOutputDeserializerResolver(
        [
            new RacClusterOutputV1Deserializer(
                new RacKeyValueOutputDeserializer())
        ]));

    [Fact]
    public void Parse_successful_complete_output_returns_versioned_snapshot()
    {
        var snapshot = _adapter.Parse(
            new Version(8, 3, 27, 2214),
            SuccessfulExecution(CreateClusterOutput()));

        Assert.Equal(SnapshotCompleteness.Complete, snapshot.Completeness);
        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal("8.3.27.2214", snapshot.SourceVersion);
        Assert.Single(snapshot.Items);
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
            SuccessfulExecution(CreateClusterOutput()));

        Assert.Equal(SnapshotCompleteness.Complete, snapshot.Completeness);
    }

    [Fact]
    public void Parse_version_below_minimum_rejects_snapshot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _adapter.Parse(
            new Version(8, 3, 27, 2213),
            SuccessfulExecution(CreateClusterOutput())));
    }

    [Fact]
    public void Parse_timed_out_execution_rejects_snapshot()
    {
        var execution = SuccessfulExecution(string.Empty) with
        {
            Outcome = RacExecutionOutcome.Unknown,
            ExitCode = -1,
            TimedOut = true
        };

        Assert.Throws<RasGateClientException>(() => _adapter.Parse(
            new Version(8, 3, 27, 2214),
            execution));
    }

    [Fact]
    public void Parse_empty_successful_output_does_not_claim_complete_snapshot()
    {
        var snapshot = _adapter.Parse(
            new Version(8, 3, 27, 2214),
            SuccessfulExecution(string.Empty));

        Assert.Equal(SnapshotCompleteness.Unknown, snapshot.Completeness);
        Assert.Empty(snapshot.Items);
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

    private static string CreateClusterOutput()
    {
        return
            "cluster : 820d1955-349e-4173-9092-a3f206d328f7\n" +
            "host : localhost\n" +
            "port : 1541\n" +
            "name : Cluster\n" +
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
}
