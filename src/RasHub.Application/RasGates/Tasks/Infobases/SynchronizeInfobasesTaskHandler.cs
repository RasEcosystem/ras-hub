using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Tasks.Infobases;

public sealed class SynchronizeInfobasesTaskHandler(
    IRepository<RasGate> rasGateRepository,
    IRepository<RasCluster> rasClusterRepository,
    IRasGateSyncPublisher publisher,
    IRasInfobaseGateway infobaseGateway,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<SynchronizeInfobasesTask>
{
    public async Task ExecuteAsync(
        SynchronizeInfobasesTask task,
        CancellationToken cancellationToken)
    {
        var rasGate = await rasGateRepository.GetByIdAsync(
            task.RasGateId,
            cancellationToken);

        if (rasGate is null)
            throw new NonRetryableBackgroundTaskException(
                $"RasGate '{task.RasGateId}' was not found.");

        if (!rasGate.IsActive)
            throw new RasGateInactiveException(rasGate.Id);

        await EnsureClusterExistsAsync(task, cancellationToken);

        var configurationRevision = rasGate.ConfigurationRevision;
        var capabilities = await infobaseGateway.GetCapabilitiesAsync(
            rasGate,
            cancellationToken);

        if (!capabilities.Supports("infobases", "snapshot"))
            throw new RasGateCapabilityNotSupportedException(
                rasGate.Id,
                "infobases",
                "snapshot");

        var snapshot = await infobaseGateway.GetInfobasesAsync(
            rasGate,
            task.ClusterId,
            task.ClusterUser,
            task.ClusterPassword,
            cancellationToken);

        if (snapshot.Completeness != SnapshotCompleteness.Complete)
            throw new RasGateClientException(
                "RasGate returned an incomplete infobase snapshot.");

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await publisher.TryPublishInfobasesAsync(
                rasGate.Id,
                configurationRevision,
                task.ClusterId,
                snapshot.Items,
                observedAt,
                cancellationToken))
            throw new RasGateConfigurationChangedException(rasGate.Id);
    }

    private async Task EnsureClusterExistsAsync(
        SynchronizeInfobasesTask task,
        CancellationToken cancellationToken)
    {
        var clusters = await rasClusterRepository.ListAsync(
            cluster => cluster.RasGateId == task.RasGateId &&
                       cluster.ExternalId == task.ClusterId,
            cancellationToken);

        if (clusters.Count != 1)
            throw new NonRetryableBackgroundTaskException(
                $"RasCluster '{task.ClusterId}' was not found for RasGate " +
                $"'{task.RasGateId}'.");
    }
}