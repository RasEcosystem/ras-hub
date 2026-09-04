using RasHub.Application.RasEndpoints.Services;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed class CreateClusterTaskHandler(
    RasEndpointExecutionTargetResolver targetResolver,
    IRasEndpointSyncPublisher publisher,
    IRasClusterGateway clusterGateway,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<CreateClusterTask, Guid>
{
    public async Task<Guid> ExecuteAsync(
        CreateClusterTask task,
        CancellationToken cancellationToken)
    {
        var target = await targetResolver.ResolveAsync(
            task.RasEndpointId,
            cancellationToken);
        var guard = target.CaptureGuard();
        var capabilities = await clusterGateway.GetCapabilitiesAsync(
            target.Gate,
            cancellationToken);

        if (!capabilities.Supports("clusters", "insert"))
            throw new RasGateCapabilityNotSupportedException(
                target.Gate.Id,
                "clusters",
                "insert");

        if (!capabilities.Supports("clusters", "info"))
            throw new RasGateCapabilityNotSupportedException(
                target.Gate.Id,
                "clusters",
                "info");

        var clusterId = await clusterGateway.CreateClusterAsync(
            target,
            task.Options,
            cancellationToken);
        RasClusterSnapshot snapshot;

        try
        {
            snapshot = await clusterGateway.GetClusterAsync(
                target,
                clusterId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            throw new RasGateMutationReadBackNotConfirmedException(
                target.Gate.Id,
                "clusters",
                "insert",
                clusterId,
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
                "insert",
                clusterId,
                exception);
        }

        if (!published)
            throw new RasGateMutationPublicationNotConfirmedException(
                target.Gate.Id,
                "clusters",
                "insert",
                clusterId);

        return clusterId;
    }
}
