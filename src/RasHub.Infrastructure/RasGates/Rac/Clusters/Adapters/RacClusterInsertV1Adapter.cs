using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Commands;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters.Adapters;

public sealed class RacClusterInsertV1Adapter(
    RacKeyValueOutputDeserializer deserializer)
    : IRacResultCommandAdapter<RasClusterCreationOptions, Guid>
{
    public string Resource => "clusters";

    public string Operation => "insert";

    public int SchemaVersion => 1;

    public Version MinimumVersion { get; } = new(8, 3, 27, 2214);

    public IReadOnlyList<string> CreateCommand(RasClusterCreationOptions command)
    {
        ValidateCommand(command);
        var arguments = new List<string> { "cluster", "insert", $"--host={command.Host}", $"--port={command.Port}" };
        RacClusterCommandArguments.AddMutableSettings(arguments, command);
        return arguments;
    }

    public Guid Parse(
        Version racVersion,
        RacExecutionResult execution,
        RasClusterCreationOptions command)
    {
        ValidateCommand(command);

        RacExecutionGuard.EnsureSucceeded(
            racVersion,
            MinimumVersion,
            execution,
            "cluster insert");

        IReadOnlyList<RacKeyValueRecord> records;

        try
        {
            records = deserializer.Deserialize(execution.StandardOutput);
        }
        catch (RacOutputDeserializationException exception)
        {
            throw new RasGateClientException(
                "RAC cluster insert command returned invalid output.",
                exception);
        }

        if (records.Count != 1 ||
            records[0].Values.Count != 1 ||
            !records[0].Values.TryGetValue("cluster", out var rawClusterId) ||
            !Guid.TryParse(rawClusterId, out var clusterId) ||
            clusterId == Guid.Empty)
            throw new RasGateClientException(
                "RAC cluster insert command did not return a valid cluster ID.",
                new RacOutputDeserializationException(
                    "RAC cluster insert output does not contain exactly one " +
                    "valid cluster ID."));

        return clusterId;
    }

    private static void ValidateCommand(RasClusterCreationOptions command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Host))
            throw new ArgumentException("A cluster host is required.", nameof(command));

        if (command.Port is < 1 or > 65_535)
            throw new ArgumentOutOfRangeException(nameof(command), command.Port, null);

        if (command.AgentPassword is not null && command.AgentUser is null)
            throw new ArgumentException(
                "An agent user is required when an agent password is provided.",
                nameof(command));
    }
}
