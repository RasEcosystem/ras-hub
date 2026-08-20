using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Infrastructure.RasGates.Client;

internal sealed class RasGateStatusGateway(
    RasGateSessionFactory sessionFactory)
    : IRasGateStatusGateway
{
    public Task<RasGateStatus> GetStatusAsync(
        RasGate rasGate,
        CancellationToken cancellationToken)
    {
        return sessionFactory
            .Create(rasGate)
            .GetStatusAsync(cancellationToken);
    }
}