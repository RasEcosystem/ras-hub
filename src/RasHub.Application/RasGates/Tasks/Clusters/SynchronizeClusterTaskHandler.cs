using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
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
            throw new NonRetryableBackgroundTaskException(
                $"RasGate '{task.RasGateId}' was not found.");

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

        var snapshot = await clusterGateway.GetClusterAsync(
            rasGate,
            task.ClusterId,
            cancellationToken);
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