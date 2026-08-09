using RasHub.Application.RasGates.Models;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasGateClient
{
    Task<RasGateStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RasClusterSnapshot>> GetClustersAsync(
        CancellationToken cancellationToken);
}