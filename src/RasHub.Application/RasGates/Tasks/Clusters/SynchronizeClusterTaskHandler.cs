using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed class SynchronizeClusterTaskHandler(
    RasEndpointExecutionTargetResolver targetResolver,
    IRasEndpointSyncPublisher publisher,
    IRasClusterGateway clusterGateway,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<SynchronizeClusterTask>
{
    public async Task ExecuteAsync(
        SynchronizeClusterTask task,
        CancellationToken cancellationToken)
    {
        var target = await targetResolver.ResolveAsync(
            task.RasEndpointId,
            cancellationToken);
        var guard = target.CaptureGuard();
        var capabilities = await clusterGateway.GetCapabilitiesAsync(
            target.Gate,
            cancellationToken);

        if (!capabilities.Supports("clusters", "info"))
            throw new RasGateCapabilityNotSupportedException(
                target.Gate.Id,
                "clusters",
                "info");

        RasClusterSnapshot snapshot;

        try
        {
            snapshot = await clusterGateway.GetClusterAsync(
                target,
                task.ClusterId,
                cancellationToken);
        }
        catch (RacResourceNotFoundException exception)
            when (exception.Resource == "clusters" &&
                  exception.ExternalId == task.ClusterId)
        {
            var removed = await publisher.TryRemoveClusterAsync(
                guard,
                task.ClusterId,
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);

            if (!removed)
                throw new RasEndpointConfigurationChangedException(
                    task.RasEndpointId);

            throw;
        }

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await publisher.TryPublishClusterAsync(
                guard,
                snapshot,
                observedAt,
                cancellationToken))
            throw new RasEndpointConfigurationChangedException(
                task.RasEndpointId);
    }
}
