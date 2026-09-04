using RasHub.Application.RasEndpoints.Services;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed class UpdateClusterTaskHandler(
    RasEndpointExecutionTargetResolver targetResolver,
    IRasEndpointSyncPublisher publisher,
    IRasClusterGateway clusterGateway,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<UpdateClusterTask>
{
    public async Task ExecuteAsync(
        UpdateClusterTask task,
        CancellationToken cancellationToken)
    {
        var target = await targetResolver.ResolveAsync(
            task.RasEndpointId,
            cancellationToken);
        var guard = target.CaptureGuard();
        var capabilities = await clusterGateway.GetCapabilitiesAsync(
            target.Gate,
            cancellationToken);

        if (!capabilities.Supports("clusters", "update"))
            throw new RasGateCapabilityNotSupportedException(
                target.Gate.Id,
                "clusters",
                "update");

        if (!capabilities.Supports("clusters", "info"))
            throw new RasGateCapabilityNotSupportedException(
                target.Gate.Id,
                "clusters",
                "info");

        await clusterGateway.UpdateClusterAsync(
            target,
            task.ClusterId,
            task.Options,
            cancellationToken);
        RasClusterSnapshot snapshot;

        try
        {
            snapshot = await clusterGateway.GetClusterAsync(
                target,
                task.ClusterId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            throw new RasGateMutationReadBackNotConfirmedException(
                target.Gate.Id,
                "clusters",
                "update",
                task.ClusterId,
                exception);
        }

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        bool published;

        try
        {
            published = await publisher.TryPublishClusterAsync(
                guard,
                snapshot,
                observedAt,
                cancellationToken);
        }
        catch (Exception exception)
        {
            throw new RasGateMutationPublicationNotConfirmedException(
                target.Gate.Id,
                "clusters",
                "update",
                task.ClusterId,
                exception);
        }

        if (!published)
            throw new RasGateMutationPublicationNotConfirmedException(
                target.Gate.Id,
                "clusters",
                "update",
                task.ClusterId);
    }
}
