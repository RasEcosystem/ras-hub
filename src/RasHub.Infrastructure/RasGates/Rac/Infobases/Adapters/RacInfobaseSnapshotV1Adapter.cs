using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Commands;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Deserialization;

namespace RasHub.Infrastructure.RasGates.Rac.Infobases.Adapters;

public sealed class RacInfobaseSnapshotV1Adapter(
    RacInfobaseOutputDeserializerResolver deserializerResolver)
    : IRacResultCommandAdapter<
        RacInfobaseQuery,
        RasResourceSnapshot<RasInfobaseSnapshot>>
{
    public string Resource => "infobases";

    public string Operation => "snapshot";

    public int SchemaVersion => 1;

    public Version MinimumVersion { get; } = new(8, 3, 27, 2214);

    public int GetSchemaVersion(Version racVersion)
    {
        return deserializerResolver.Resolve(racVersion).SchemaVersion;
    }

    public IReadOnlyList<string> CreateCommand(RacInfobaseQuery command)
    {
        RacInfobaseCommandArguments.Validate(command);

        if (command.InfobaseId is not null)
            throw new ArgumentException(
                "A complete infobase snapshot does not accept an infobase ID.",
                nameof(command));

        var arguments = new List<string> { "infobase", "summary", "list" };
        RacInfobaseCommandArguments.AddCluster(arguments, command);
        return arguments;
    }

    public RasResourceSnapshot<RasInfobaseSnapshot> Parse(
        Version racVersion,
        RacExecutionResult execution,
        RacInfobaseQuery command)
    {
        RacInfobaseCommandArguments.Validate(command);

        if (command.InfobaseId is not null)
            throw new ArgumentException(
                "A complete infobase snapshot does not accept an infobase ID.",
                nameof(command));

        RacExecutionGuard.EnsureSucceeded(
            racVersion,
            MinimumVersion,
            execution,
            "infobase summary list");

        var deserializer = deserializerResolver.Resolve(racVersion);
        var items = deserializer.Deserialize(execution.StandardOutput);

        return new RasResourceSnapshot<RasInfobaseSnapshot>
        {
            SchemaVersion = deserializer.SchemaVersion,
            SourceVersion = racVersion.ToString(),
            Completeness = items.Count == 0
                ? SnapshotCompleteness.Unknown
                : SnapshotCompleteness.Complete,
            Items = items
        };
    }
}
