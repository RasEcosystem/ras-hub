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
}