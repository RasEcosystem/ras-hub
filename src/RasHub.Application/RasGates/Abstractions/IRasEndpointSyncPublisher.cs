using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasGates.Models;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasEndpointSyncPublisher
{
    Task<bool> TryPublishClustersAsync(
        RasEndpointExecutionGuard guard,
        IReadOnlyList<RasClusterSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> TryPublishClusterAsync(
        RasEndpointExecutionGuard guard,
        RasClusterSnapshot snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> TryPublishInfobasesAsync(
        RasEndpointExecutionGuard guard,
        Guid clusterId,
        IReadOnlyList<RasInfobaseSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> TryPublishInfobaseAsync(
        RasEndpointExecutionGuard guard,
        Guid clusterId,
        RasInfobaseSnapshot snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> TryRemoveClusterAsync(
        RasEndpointExecutionGuard guard,
        Guid clusterId,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> TryRemoveInfobaseAsync(
        RasEndpointExecutionGuard guard,
        Guid clusterId,
        Guid infobaseId,
        DateTime observedAt,
        CancellationToken cancellationToken);
}
