using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasGateStatusGateway
{
    Task<RasGateStatus> GetStatusAsync(
        RasGate rasGate,
        CancellationToken cancellationToken);
}
