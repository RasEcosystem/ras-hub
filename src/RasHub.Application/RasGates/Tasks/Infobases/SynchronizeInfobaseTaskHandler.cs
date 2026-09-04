using RasHub.Application.Interfaces;
using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Tasks.Infobases;

public sealed class SynchronizeInfobaseTaskHandler(
    RasEndpointExecutionTargetResolver targetResolver,
    IRepository<RasCluster> rasClusterRepository,
    IRasEndpointSyncPublisher publisher,
    IRasInfobaseGateway infobaseGateway,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<SynchronizeInfobaseTask>
{
    public async Task ExecuteAsync(
        SynchronizeInfobaseTask task,
        CancellationToken cancellationToken)
    {
        var target = await targetResolver.ResolveAsync(
            task.RasEndpointId,
            cancellationToken);
        await EnsureClusterExistsAsync(task, cancellationToken);

        var guard = target.CaptureGuard();
        var capabilities = await infobaseGateway.GetCapabilitiesAsync(
            target.Gate,
            cancellationToken);

        if (!capabilities.Supports("infobases", "info"))
            throw new RasGateCapabilityNotSupportedException(
                target.Gate.Id,
                "infobases",
                "info");

        RasInfobaseSnapshot snapshot;

        try
        {
            snapshot = await infobaseGateway.GetInfobaseAsync(
                target,
                task.ClusterId,
                task.InfobaseId,
                task.ClusterUser,
                task.ClusterPassword,
                cancellationToken);
        }
        catch (RacResourceNotFoundException exception)
            when (exception.Resource == "infobases" &&
                  exception.ExternalId == task.InfobaseId)
        {
            var removed = await publisher.TryRemoveInfobaseAsync(
                guard,
                task.ClusterId,
                task.InfobaseId,
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);

            if (!removed)
                throw new RasEndpointConfigurationChangedException(
                    task.RasEndpointId);

            throw;
        }

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await publisher.TryPublishInfobaseAsync(
                guard,
                task.ClusterId,
                snapshot,
                observedAt,
                cancellationToken))
            throw new RasEndpointConfigurationChangedException(
                task.RasEndpointId);
    }

    private async Task EnsureClusterExistsAsync(
        SynchronizeInfobaseTask task,
        CancellationToken cancellationToken)
    {
        var clusters = await rasClusterRepository.ListAsync(
            cluster => cluster.RasEndpointId == task.RasEndpointId &&
                       cluster.ExternalId == task.ClusterId,
            cancellationToken);

        if (clusters.Count != 1)
            throw new RasClusterNotFoundException(
                task.RasEndpointId,
                task.ClusterId);
    }
}
