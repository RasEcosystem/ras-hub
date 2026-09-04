using RasHub.Application.Interfaces;
using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Tasks.Infobases;

public sealed class SynchronizeInfobasesTaskHandler(
    RasEndpointExecutionTargetResolver targetResolver,
    IRepository<RasCluster> rasClusterRepository,
    IRasEndpointSyncPublisher publisher,
    IRasInfobaseGateway infobaseGateway,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<
        SynchronizeInfobasesTask,
        CollectionSynchronizationResult>
{
    public async Task<CollectionSynchronizationResult> ExecuteAsync(
        SynchronizeInfobasesTask task,
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

        if (!capabilities.Supports("infobases", "snapshot"))
            throw new RasGateCapabilityNotSupportedException(
                target.Gate.Id,
                "infobases",
                "snapshot");

        var snapshot = await infobaseGateway.GetInfobasesAsync(
            target,
            task.ClusterId,
            task.ClusterUser,
            task.ClusterPassword,
            cancellationToken);

        if (snapshot.Completeness != SnapshotCompleteness.Complete)
            throw new RasGateClientException(
                "RasGate returned an incomplete infobase snapshot.");

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await publisher.TryPublishInfobasesAsync(
                guard,
                task.ClusterId,
                snapshot.Items,
                observedAt,
                cancellationToken))
            throw new RasEndpointConfigurationChangedException(
                task.RasEndpointId);

        return new CollectionSynchronizationResult(
            snapshot.Items.Count,
            observedAt);
    }

    private async Task EnsureClusterExistsAsync(
        SynchronizeInfobasesTask task,
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
