using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Commands;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Deserialization;

namespace RasHub.Infrastructure.RasGates.Rac.Infobases.Adapters;

public sealed class RacInfobaseInfoV1Adapter(
    RacInfobaseOutputDeserializerResolver deserializerResolver)
    : IRacResultCommandAdapter<
        RacInfobaseQuery,
        RasResourceSnapshot<RasInfobaseSnapshot>>
{
    public string Resource => "infobases";

    public string Operation => "info";

    public int SchemaVersion => 1;

    public Version MinimumVersion { get; } = new(8, 3, 27, 2214);

    public int GetSchemaVersion(Version racVersion)
    {
        return deserializerResolver.Resolve(racVersion).SchemaVersion;
    }

    public IReadOnlyList<string> CreateCommand(RacInfobaseQuery command)
    {
        var infobaseId = GetRequiredInfobaseId(command);
        var arguments = new List<string> { "infobase", "summary", "info" };
        RacInfobaseCommandArguments.AddCluster(arguments, command);
        arguments.Add($"--infobase={infobaseId:D}");
        return arguments;
    }

    public RasResourceSnapshot<RasInfobaseSnapshot> Parse(
        Version racVersion,
        RacExecutionResult execution,
        RacInfobaseQuery command)
    {
        var infobaseId = GetRequiredInfobaseId(command);

        RacExecutionGuard.EnsureSucceeded(
            racVersion,
            MinimumVersion,
            execution,
            "infobase summary info");

        var deserializer = deserializerResolver.Resolve(racVersion);
        var items = deserializer.Deserialize(execution.StandardOutput);

        if (items.Count == 0)
            throw new RacResourceNotFoundException("infobases", infobaseId);

        if (items.Count != 1)
            throw new RasGateClientException(
                "RAC infobase summary info command did not return exactly " +
                "one infobase.");

        if (items[0].ExternalId != infobaseId)
            throw new RasGateClientException(
                "RAC infobase summary info command returned a different " +
                "infobase.");

        return new RasResourceSnapshot<RasInfobaseSnapshot>
        {
            SchemaVersion = deserializer.SchemaVersion,
            SourceVersion = racVersion.ToString(),
            Completeness = SnapshotCompleteness.Complete,
            Items = items
        };
    }

    private static Guid GetRequiredInfobaseId(RacInfobaseQuery command)
    {
        RacInfobaseCommandArguments.Validate(command);

        if (command.InfobaseId is { } infobaseId)
            return infobaseId;

        throw new ArgumentException(
            "A non-empty infobase external ID is required.",
            nameof(command));
    }
}