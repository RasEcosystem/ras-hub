using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Commands;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Deserialization;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Infobases.Adapters;

public sealed class RacInfobaseSnapshotV1AdapterTests
{
    private static readonly Guid ClusterId =
        Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7");

    private readonly RacInfobaseSnapshotV1Adapter _adapter = new(
        new RacInfobaseOutputDeserializerResolver(
        [
            new RacInfobaseOutputV1Deserializer(
                new RacKeyValueOutputDeserializer())
        ]));

    [Fact]
    public void Create_command_with_credentials_returns_summary_list_arguments()
    {
        var query = new RacInfobaseQuery(
            ClusterId,
            clusterUser: "cluster-admin",
            clusterPassword: "cluster-secret");

        var arguments = _adapter.CreateCommand(query);

        Assert.Equal(
            [
                "infobase",
                "summary",
                "list",
                $"--cluster={ClusterId:D}",
                "--cluster-user=cluster-admin",
                "--cluster-pwd=cluster-secret"
            ],
            arguments);
        Assert.Equal(nameof(RacInfobaseQuery), query.ToString());
        Assert.DoesNotContain("cluster-secret", query.ToString());
    }

    [Fact]
    public void Parse_successful_output_returns_complete_versioned_snapshot()
    {
        var snapshot = _adapter.Parse(
            new Version(8, 3, 27, 2214),
            SuccessfulExecution(
                "infobase : 85f82b58-d02c-4f40-9ad3-2131adf31e48\n" +
                "name : rim_next\n" +
                "descr : \n"),
            new RacInfobaseQuery(ClusterId));

        Assert.Equal(SnapshotCompleteness.Complete, snapshot.Completeness);
        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal("8.3.27.2214", snapshot.SourceVersion);
        Assert.Single(snapshot.Items);
    }

    [Fact]
    public void Parse_empty_output_returns_unknown_snapshot()
    {
        var snapshot = _adapter.Parse(
            new Version(8, 3, 27, 2214),
            SuccessfulExecution(string.Empty),
            new RacInfobaseQuery(ClusterId));

        Assert.Equal(SnapshotCompleteness.Unknown, snapshot.Completeness);
        Assert.Empty(snapshot.Items);
    }

    [Fact]
    public void Create_command_with_infobase_id_rejects_targeted_query()
    {
        Assert.Throws<ArgumentException>(() =>
            _adapter.CreateCommand(new RacInfobaseQuery(
                ClusterId,
                Guid.NewGuid())));
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
}
