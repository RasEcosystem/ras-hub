using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasClusterGateway
{
    Task<RasGateCapabilities> GetCapabilitiesAsync(
        RasGate rasGate,
        CancellationToken cancellationToken);

    Task<RasResourceSnapshot<RasClusterSnapshot>> GetClustersAsync(
        RasGate rasGate,
        CancellationToken cancellationToken);

    Task<RasClusterSnapshot> GetClusterAsync(
        RasGate rasGate,
        Guid clusterId,
        CancellationToken cancellationToken);

    Task<Guid> CreateClusterAsync(
        RasGate rasGate,
        RasClusterCreationOptions options,
        CancellationToken cancellationToken);

    Task UpdateClusterAsync(
        RasGate rasGate,
        Guid clusterId,
        RasClusterUpdateOptions options,
        CancellationToken cancellationToken);

    Task RemoveClusterAsync(
        RasGate rasGate,
        Guid clusterId,
        string? clusterUser,
        string? clusterPassword,
        CancellationToken cancellationToken);
}