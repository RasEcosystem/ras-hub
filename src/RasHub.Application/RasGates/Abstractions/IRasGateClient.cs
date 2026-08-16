using RasHub.Application.RasGates.Models;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasGateClient
{
    Task<RasGateStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<RasGateCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken);

    Task<RasResourceSnapshot<RasClusterSnapshot>> GetClustersAsync(
        CancellationToken cancellationToken);

    Task<RasClusterSnapshot> GetClusterAsync(
        Guid clusterId,
        CancellationToken cancellationToken);

    Task<Guid> CreateClusterAsync(
        RasClusterCreationOptions options,
        CancellationToken cancellationToken);

    Task UpdateClusterAsync(
        Guid clusterId,
        RasClusterUpdateOptions options,
        CancellationToken cancellationToken);

    Task RemoveClusterAsync(
        Guid clusterId,
        string? clusterUser,
        string? clusterPassword,
        CancellationToken cancellationToken);
}