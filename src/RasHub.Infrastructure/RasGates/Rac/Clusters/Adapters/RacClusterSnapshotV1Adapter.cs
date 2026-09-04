using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Deserialization;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters.Adapters;

public sealed class RacClusterSnapshotV1Adapter(
    RacClusterOutputDeserializerResolver deserializerResolver)
    : IRacResourceAdapter<RasClusterSnapshot>
{
    public string Resource => "clusters";

    public string Operation => "snapshot";

    public int SchemaVersion => 1;

    public Version MinimumVersion { get; } = new(8, 3, 27, 2214);

    public int GetSchemaVersion(Version racVersion)
    {
        return deserializerResolver.Resolve(racVersion).SchemaVersion;
    }

    public IReadOnlyList<string> CreateCommand(Guid? externalId = null)
    {
        if (externalId is not null)
            throw new ArgumentException(
                "A complete cluster snapshot does not accept an external ID.",
                nameof(externalId));

        return ["cluster", "list"];
    }

    public RasResourceSnapshot<RasClusterSnapshot> Parse(
        Version racVersion,
        RacExecutionResult execution,
        Guid? externalId = null)
    {
        if (externalId is not null)
            throw new ArgumentException(
                "A complete cluster snapshot does not accept an external ID.",
                nameof(externalId));

        RacExecutionGuard.EnsureSucceeded(
            racVersion,
            MinimumVersion,
            execution,
            "cluster list");

        var deserializer = deserializerResolver.Resolve(racVersion);
        var items = deserializer.Deserialize(execution.StandardOutput);

        return new RasResourceSnapshot<RasClusterSnapshot>
        {
            SchemaVersion = deserializer.SchemaVersion,
            SourceVersion = racVersion.ToString(),
            Completeness = SnapshotCompleteness.Complete,
            Items = items
        };
    }
}
