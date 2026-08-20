using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Commands;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Deserialization;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Infobases.Adapters;

public sealed class RacInfobaseInfoV1AdapterTests
{
    private static readonly Guid ClusterId =
        Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7");

    private static readonly Guid InfobaseId =
        Guid.Parse("85f82b58-d02c-4f40-9ad3-2131adf31e48");

    private readonly RacInfobaseInfoV1Adapter _adapter = new(
        new RacInfobaseOutputDeserializerResolver(
        [
            new RacInfobaseOutputV1Deserializer(
                new RacKeyValueOutputDeserializer())
        ]));

    [Fact]
    public void Create_command_returns_targeted_summary_info_arguments()
    {
        var arguments = _adapter.CreateCommand(
            new RacInfobaseQuery(ClusterId, InfobaseId));

        Assert.Equal(
            [
                "infobase",
                "summary",
                "info",
                $"--cluster={ClusterId:D}",
                $"--infobase={InfobaseId:D}"
            ],
            arguments);
    }

    [Fact]
    public void Parse_single_matching_infobase_returns_complete_snapshot()
    {
        var snapshot = _adapter.Parse(
            new Version(8, 3, 27, 2214),
            SuccessfulExecution(InfobaseId),
            new RacInfobaseQuery(ClusterId, InfobaseId));

        Assert.Equal(SnapshotCompleteness.Complete, snapshot.Completeness);
        Assert.Equal(InfobaseId, Assert.Single(snapshot.Items).ExternalId);
    }

    [Fact]
    public void Parse_different_infobase_rejects_output()
    {
        Assert.Throws<RasGateClientException>(() =>
            _adapter.Parse(
                new Version(8, 3, 27, 2214),
                SuccessfulExecution(Guid.NewGuid()),
                new RacInfobaseQuery(ClusterId, InfobaseId)));
    }

    [Fact]
    public void Parse_empty_successful_output_reports_missing_infobase()
    {
        var execution = SuccessfulExecution(InfobaseId) with { StandardOutput = string.Empty };

        var exception = Assert.Throws<RacResourceNotFoundException>(() =>
            _adapter.Parse(
                new Version(8, 3, 27, 2214),
                execution,
                new RacInfobaseQuery(ClusterId, InfobaseId)));

        Assert.Equal("infobases", exception.Resource);
        Assert.Equal(InfobaseId, exception.ExternalId);
    }

    [Fact]
    public void Create_command_without_infobase_id_rejects_query()
    {
        Assert.Throws<ArgumentException>(() =>
            _adapter.CreateCommand(new RacInfobaseQuery(ClusterId)));
    }

    private static RacExecutionResult SuccessfulExecution(Guid infobaseId)
    {
        return new RacExecutionResult
        {
            Outcome = RacExecutionOutcome.Succeeded,
            ExitCode = 0,
            StandardOutput =
                $"infobase : {infobaseId:D}\n" +
                "name : rim_next\n" +
                "descr : \n",
            StandardError = string.Empty,
            DurationMilliseconds = 1,
            TimedOut = false
        };
    }
}