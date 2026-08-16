namespace RasHub.Application.RasGates.Models;

public sealed record RasGateCapabilities
{
    public required string RacVersion { get; init; }

    public required IReadOnlyList<RasResourceCapability> Resources { get; init; }

    public bool Supports(string resource, string operation)
    {
        return Resources.Any(capability =>
            string.Equals(
                capability.Resource,
                resource,
                StringComparison.Ordinal) &&
            string.Equals(
                capability.Operation,
                operation,
                StringComparison.Ordinal));
    }
}

public sealed record RasResourceCapability(
    string Resource,
    string Operation,
    int SchemaVersion);