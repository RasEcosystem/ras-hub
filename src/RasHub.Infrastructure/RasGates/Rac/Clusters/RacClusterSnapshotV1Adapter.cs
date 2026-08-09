using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac.Adapters;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters;

public sealed class RacClusterSnapshotV1Adapter(
    RacClusterOutputDeserializer deserializer)
    : IRacResourceAdapter<RasClusterSnapshot>
{
    private static readonly Version BaselineVersion = new(8, 3, 27, 2214);
    private static readonly Version NextPlatformFamilyVersion = new(8, 4, 0, 0);

    public string Resource => "clusters";

    public string Operation => "snapshot";

    public int SchemaVersion => 1;

    public bool Supports(Version racVersion)
    {
        return racVersion >= BaselineVersion &&
               racVersion < NextPlatformFamilyVersion;
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

        if (!Supports(racVersion))
            throw new ArgumentOutOfRangeException(
                nameof(racVersion),
                racVersion,
                "The RAC version is not supported by this adapter.");

        if (execution.TimedOut)
            throw new RasGateClientException(
                "RAC cluster list command timed out.");

        if (execution.ExitCode != 0)
            throw new RasGateClientException(
                $"RAC cluster list command failed with exit code " +
                $"{execution.ExitCode}.");

        var items = deserializer.Deserialize(execution.StandardOutput);

        return new RasResourceSnapshot<RasClusterSnapshot>
        {
            SchemaVersion = SchemaVersion,
            SourceVersion = racVersion.ToString(),
            Completeness = items.Count == 0
                ? SnapshotCompleteness.Unknown
                : SnapshotCompleteness.Complete,
            Items = items
        };
    }
}