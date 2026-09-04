using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed class SynchronizeClustersTaskHandler(
    RasEndpointExecutionTargetResolver targetResolver,
    IRasEndpointSyncPublisher publisher,
    IRasClusterGateway clusterGateway,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<
        SynchronizeClustersTask,
        CollectionSynchronizationResult>
{
    public async Task<CollectionSynchronizationResult> ExecuteAsync(
        SynchronizeClustersTask task,
        CancellationToken cancellationToken)
    {
        var target = await targetResolver.ResolveAsync(
            task.RasEndpointId,
            cancellationToken);
        var guard = target.CaptureGuard();
        var capabilities = await clusterGateway.GetCapabilitiesAsync(
            target.Gate,
            cancellationToken);

        if (!capabilities.Supports("clusters", "snapshot"))
            throw new RasGateCapabilityNotSupportedException(
                target.Gate.Id,
                "clusters",
                "snapshot");

        var snapshot = await clusterGateway.GetClustersAsync(
            target,
            cancellationToken);

        if (snapshot.Completeness != SnapshotCompleteness.Complete)
            throw new RasGateClientException(
                "RasGate returned an incomplete cluster snapshot.");

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await publisher.TryPublishClustersAsync(
                guard,
                snapshot.Items,
                observedAt,
                cancellationToken))
            throw new RasEndpointConfigurationChangedException(
                task.RasEndpointId);

        return new CollectionSynchronizationResult(
            snapshot.Items.Count,
            observedAt);
    }
}
