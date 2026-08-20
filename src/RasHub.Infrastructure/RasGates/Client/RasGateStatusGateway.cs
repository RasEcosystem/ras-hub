using Microsoft.Extensions.Logging;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Infrastructure.RasGates.Client;

internal sealed class RasGateStatusGateway(
    RasGateSessionFactory sessionFactory,
    ILogger<RasGateStatusGateway> logger)
    : IRasGateStatusGateway
{
    public async Task<RasGateStatus> GetStatusAsync(
        RasGate rasGate,
        CancellationToken cancellationToken)
    {
        var session = sessionFactory.Create(rasGate);
        var status = await session.GetStatusAsync(cancellationToken);

        try
        {
            var racStatus = await session.GetRacStatusAsync(cancellationToken);

            return status with { RacAvailable = racStatus.Available, RacVersion = racStatus.Version?.ToString() };
        }
        catch (RasGateClientException)
        {
            logger.LogWarning(
                "RAC status could not be observed for RasGate {RasGateId}",
                rasGate.Id);

            return status with { RacAvailable = null, RacVersion = null };
        }
    }
}
