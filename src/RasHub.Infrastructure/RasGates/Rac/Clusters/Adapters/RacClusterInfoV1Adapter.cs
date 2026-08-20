using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Deserialization;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters.Adapters;

public sealed class RacClusterInfoV1Adapter(
    RacClusterOutputDeserializerResolver deserializerResolver)
    : IRacResourceAdapter<RasClusterSnapshot>
{
    public string Resource => "clusters";

    public string Operation => "info";

    public int SchemaVersion => 1;

    public Version MinimumVersion { get; } = new(8, 3, 27, 2214);

    public int GetSchemaVersion(Version racVersion)
    {
        return deserializerResolver.Resolve(racVersion).SchemaVersion;
    }

    public IReadOnlyList<string> CreateCommand(Guid? externalId = null)
    {
        var clusterId = GetRequiredExternalId(externalId);

        return ["cluster", "info", $"--cluster={clusterId:D}"];
    }

    public RasResourceSnapshot<RasClusterSnapshot> Parse(
        Version racVersion,
        RacExecutionResult execution,
        Guid? externalId = null)
    {
        var clusterId = GetRequiredExternalId(externalId);

        RacExecutionGuard.EnsureSucceeded(
            racVersion,
            MinimumVersion,
            execution,
            "cluster info");

        var deserializer = deserializerResolver.Resolve(racVersion);
        var items = deserializer.Deserialize(execution.StandardOutput);

        if (items.Count != 1)
            throw new RasGateClientException(
                "RAC cluster info command did not return exactly one cluster.");

        if (items[0].ExternalId != clusterId)
            throw new RasGateClientException(
                "RAC cluster info command returned a different cluster.");

        return new RasResourceSnapshot<RasClusterSnapshot>
        {
            SchemaVersion = deserializer.SchemaVersion,
            SourceVersion = racVersion.ToString(),
            Completeness = SnapshotCompleteness.Complete,
            Items = items
        };
    }

    private static Guid GetRequiredExternalId(Guid? externalId)
    {
        if (externalId is { } value && value != Guid.Empty)
            return value;

        throw new ArgumentException(
            "A non-empty cluster external ID is required.",
            nameof(externalId));
    }
}