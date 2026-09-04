using RasHub.Application.RasEndpoints.Services;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed class RemoveClusterTaskHandler(
    RasEndpointExecutionTargetResolver targetResolver,
    IRasEndpointSyncPublisher publisher,
    IRasClusterGateway clusterGateway,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<RemoveClusterTask>
{
    public async Task ExecuteAsync(
        RemoveClusterTask task,
        CancellationToken cancellationToken)
    {
        var target = await targetResolver.ResolveAsync(
            task.RasEndpointId,
            cancellationToken);
        var guard = target.CaptureGuard();
        var capabilities = await clusterGateway.GetCapabilitiesAsync(
            target.Gate,
            cancellationToken);

        if (!capabilities.Supports("clusters", "remove"))
            throw new RasGateCapabilityNotSupportedException(
                target.Gate.Id,
                "clusters",
                "remove");

        await clusterGateway.RemoveClusterAsync(
            target,
            task.ClusterId,
            task.ClusterUser,
            task.ClusterPassword,
            cancellationToken);
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        bool published;

        try
        {
            published = await publisher.TryRemoveClusterAsync(
                guard,
                task.ClusterId,
                observedAt,
                cancellationToken);
        }
        catch (Exception exception)
        {
            throw new RasGateMutationPublicationNotConfirmedException(
                target.Gate.Id,
                "clusters",
                "remove",
                task.ClusterId,
                exception);
        }

        if (!published)
            throw new RasGateMutationPublicationNotConfirmedException(
                target.Gate.Id,
                "clusters",
                "remove",
                task.ClusterId);
    }
}
