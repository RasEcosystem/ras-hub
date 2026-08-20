using RasHub.Application.RasGates.Models;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasInfobaseSnapshotStore
{
    /// <summary>
    ///     Applies a complete authoritative infobase collection and removes
    ///     records absent from that collection.
    /// </summary>
    Task ApplyAsync(
        Guid rasClusterId,
        IReadOnlyList<RasInfobaseSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Adds or updates one observed infobase without changing sibling
    ///     records.
    /// </summary>
    Task UpsertAsync(
        Guid rasClusterId,
        RasInfobaseSnapshot snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Invalidates every cached infobase owned by one cluster.
    /// </summary>
    Task InvalidateAsync(
        Guid rasClusterId,
        CancellationToken cancellationToken);
}