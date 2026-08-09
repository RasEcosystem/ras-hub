using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac.Adapters;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters;

public sealed class RacClusterInfoV1Adapter(
    RacClusterOutputDeserializer deserializer)
    : IRacResourceAdapter<RasClusterSnapshot>
{
    private const int MaxFailureOutputLength = 1_000;

    private static readonly Version BaselineVersion = new(8, 3, 27, 2214);
    private static readonly Version NextPlatformFamilyVersion = new(8, 4, 0, 0);

    public string Resource => "clusters";

    public string Operation => "info";

    public int SchemaVersion => 1;

    public bool Supports(Version racVersion)
    {
        return racVersion >= BaselineVersion &&
               racVersion < NextPlatformFamilyVersion;
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

        if (!Supports(racVersion))
            throw new ArgumentOutOfRangeException(
                nameof(racVersion),
                racVersion,
                "The RAC version is not supported by this adapter.");

        if (execution.TimedOut)
            throw new RasGateClientException(
                "RAC cluster info command timed out.");

        if (execution.ExitCode != 0)
            throw new RasGateClientException(
                CreateFailureMessage(execution));

        var items = deserializer.Deserialize(execution.StandardOutput);

        if (items.Count != 1)
            throw new RasGateClientException(
                "RAC cluster info command did not return exactly one cluster.");

        if (items[0].ExternalId != clusterId)
            throw new RasGateClientException(
                "RAC cluster info command returned a different cluster.");

        return new RasResourceSnapshot<RasClusterSnapshot>
        {
            SchemaVersion = SchemaVersion,
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

    private static string CreateFailureMessage(RacExecutionResult execution)
    {
        var output = string.IsNullOrWhiteSpace(execution.StandardError)
            ? execution.StandardOutput
            : execution.StandardError;
        output = output
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (output.Length > MaxFailureOutputLength)
            output = $"{output[..MaxFailureOutputLength]}…";

        var details = output.Length == 0
            ? string.Empty
            : $" RAC output: {output}";

        return $"RAC cluster info command failed with exit code " +
               $"{execution.ExitCode}.{details}";
    }
}