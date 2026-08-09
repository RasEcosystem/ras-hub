using RasHub.Application.RasGates.Models;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasClusterSnapshotStore
{
    Task ApplyAsync(
        Guid rasGateId,
        IReadOnlyList<RasClusterSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken);
}