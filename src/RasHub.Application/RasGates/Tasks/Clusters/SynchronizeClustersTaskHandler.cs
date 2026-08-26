using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed class SynchronizeClustersTaskHandler(
    IRepository<RasGate> rasGateRepository,
    IRasGateSyncPublisher publisher,
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

        if (!capabilities.Supports("clusters", "snapshot"))
            throw new RasGateCapabilityNotSupportedException(
                rasGate.Id,
                "clusters",
                "snapshot");

        var snapshot = await clusterGateway.GetClustersAsync(
            rasGate,
            cancellationToken);

        if (snapshot.Completeness != SnapshotCompleteness.Complete)
            throw new RasGateClientException(
                "RasGate returned an incomplete cluster snapshot.");

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await publisher.TryPublishClustersAsync(
                rasGate.Id,
                configurationRevision,
                snapshot.Items,
                observedAt,
                cancellationToken))
            throw new RasGateConfigurationChangedException(rasGate.Id);

        return new CollectionSynchronizationResult(
            snapshot.Items.Count,
            observedAt);
    }
}
