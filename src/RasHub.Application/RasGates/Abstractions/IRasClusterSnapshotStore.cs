using RasHub.Application.RasGates.Models;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasClusterSnapshotStore
{
    /// <summary>
    ///     Applies a complete authoritative cluster collection and removes records
    ///     absent from that collection.
    /// </summary>
    Task ApplyAsync(
        Guid rasGateId,
        IReadOnlyList<RasClusterSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Adds or updates one observed cluster without changing other records.
    /// </summary>
    Task UpsertAsync(
        Guid rasGateId,
        RasClusterSnapshot snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task InvalidateAsync(
        Guid rasGateId,
        CancellationToken cancellationToken);
}