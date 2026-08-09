using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Clusters;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Clusters;

public sealed class RacClusterSnapshotV1AdapterTests
{
    private readonly RacClusterSnapshotV1Adapter _adapter = new(
        new RacClusterOutputDeserializer(
            new RacKeyValueOutputDeserializer()));

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

    [Theory]
    [InlineData("8.3.27.2213", false)]
    [InlineData("8.3.27.2214", true)]
    [InlineData("8.3.27.2215", true)]
    [InlineData("8.3.28.0", true)]
    [InlineData("8.3.99.9999", true)]
    [InlineData("8.4.0.0", false)]
    public void Supports_compatible_platform_range_returns_expected_result(
        string version,
        bool expected)
    {
        Assert.Equal(expected, _adapter.Supports(Version.Parse(version)));
    }

    [Fact]
    public void Parse_timed_out_execution_rejects_snapshot()
    {
        var execution = SuccessfulExecution(string.Empty) with
        {
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