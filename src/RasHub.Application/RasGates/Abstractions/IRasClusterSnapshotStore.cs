using RasHub.Application.RasGates.Models;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasClusterSnapshotStore
{
    /// <summary>
    ///     Applies a complete authoritative cluster collection and removes records
    ///     absent from that collection together with their cached infobases.
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

    /// <summary>
    ///     Removes one cached cluster and all of its cached infobases.
    /// </summary>
    Task RemoveAsync(
        Guid rasGateId,
        Guid clusterId,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Invalidates every cached cluster for a RasGate and all descendant
    ///     infobases.
    /// </summary>
    Task InvalidateAsync(
        Guid rasGateId,
        CancellationToken cancellationToken);
}
