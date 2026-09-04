using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasClusterGateway
{
    Task<RasGateCapabilities> GetCapabilitiesAsync(
        RasGate rasGate,
        CancellationToken cancellationToken);

    Task<RasResourceSnapshot<RasClusterSnapshot>> GetClustersAsync(
        RasEndpointExecutionTarget target,
        CancellationToken cancellationToken);

    Task<RasClusterSnapshot> GetClusterAsync(
        RasEndpointExecutionTarget target,
        Guid clusterId,
        CancellationToken cancellationToken);

    Task<Guid> CreateClusterAsync(
        RasEndpointExecutionTarget target,
        RasClusterCreationOptions options,
        CancellationToken cancellationToken);

    Task UpdateClusterAsync(
        RasEndpointExecutionTarget target,
        Guid clusterId,
        RasClusterUpdateOptions options,
        CancellationToken cancellationToken);

    Task RemoveClusterAsync(
        RasEndpointExecutionTarget target,
        Guid clusterId,
        string? clusterUser,
        string? clusterPassword,
        CancellationToken cancellationToken);
}
