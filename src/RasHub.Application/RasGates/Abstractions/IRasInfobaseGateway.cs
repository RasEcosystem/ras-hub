using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasInfobaseGateway
{
    Task<RasGateCapabilities> GetCapabilitiesAsync(
        RasGate rasGate,
        CancellationToken cancellationToken);

    Task<RasResourceSnapshot<RasInfobaseSnapshot>> GetInfobasesAsync(
        RasGate rasGate,
        Guid clusterId,
        string? clusterUser,
        string? clusterPassword,
        CancellationToken cancellationToken);

    Task<RasInfobaseSnapshot> GetInfobaseAsync(
        RasGate rasGate,
        Guid clusterId,
        Guid infobaseId,
        string? clusterUser,
        string? clusterPassword,
        CancellationToken cancellationToken);
}
