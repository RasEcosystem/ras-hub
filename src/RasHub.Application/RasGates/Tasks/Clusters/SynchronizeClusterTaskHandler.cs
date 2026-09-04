using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed class SynchronizeClusterTaskHandler(
    IRepository<RasGate> rasGateRepository,
    IRasGateSyncPublisher publisher,
    IRasClusterGateway clusterGateway,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<SynchronizeClusterTask>
{
    public async Task ExecuteAsync(
        SynchronizeClusterTask task,
        CancellationToken cancellationToken)
    {
        var rasGate = await rasGateRepository.GetByIdAsync(
            task.RasGateId,
            cancellationToken);

        if (rasGate is null)
            throw new RasGateNotFoundException(task.RasGateId);

        if (!rasGate.IsActive)
            throw new RasGateInactiveException(rasGate.Id);

        var configurationRevision = rasGate.ConfigurationRevision;
        var capabilities = await clusterGateway.GetCapabilitiesAsync(
            rasGate,
            cancellationToken);

        if (!capabilities.Supports("clusters", "info"))
            throw new RasGateCapabilityNotSupportedException(
                rasGate.Id,
                "clusters",
                "info");

        RasClusterSnapshot snapshot;

        try
        {
            snapshot = await clusterGateway.GetClusterAsync(
                rasGate,
                task.ClusterId,
                cancellationToken);
        }
        catch (RacResourceNotFoundException exception)
            when (exception.Resource == "clusters" &&
                  exception.ExternalId == task.ClusterId)
        {
            var removed = await publisher.TryRemoveClusterAsync(
                rasGate.Id,
                configurationRevision,
                task.ClusterId,
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);

            if (!removed)
                throw new RasGateConfigurationChangedException(rasGate.Id);

            throw;
        }

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await publisher.TryPublishClusterAsync(
                rasGate.Id,
                configurationRevision,
                snapshot,
                observedAt,
                cancellationToken))
            throw new RasGateConfigurationChangedException(rasGate.Id);
    }
}
