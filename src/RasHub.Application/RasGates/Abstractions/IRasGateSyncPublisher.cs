using RasHub.Application.RasGates.Models;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasGateSyncPublisher
{
    Task<bool> TryPublishStatusAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        RasGateStatus status,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> TryPublishClustersAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        IReadOnlyList<RasClusterSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> TryPublishClusterAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        RasClusterSnapshot snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken);
}