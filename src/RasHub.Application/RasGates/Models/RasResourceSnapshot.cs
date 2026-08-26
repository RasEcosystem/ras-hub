namespace RasHub.Application.RasGates.Models;

public sealed record RasResourceSnapshot<T>
{
    public required int SchemaVersion { get; init; }

    public required string SourceVersion { get; init; }

    public required SnapshotCompleteness Completeness { get; init; }

    public required IReadOnlyList<T> Items { get; init; }
}
