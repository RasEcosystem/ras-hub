using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Tasks;

public sealed class RemoveClusterTaskHandler(
    IRepository<RasGate> rasGateRepository,
    IRasGateSyncPublisher publisher,
    IRasGateClientFactory clientFactory,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<RemoveClusterTask>
{
    public async Task ExecuteAsync(
        RemoveClusterTask task,
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
        var client = clientFactory.Create(rasGate);
        var capabilities = await client.GetCapabilitiesAsync(cancellationToken);

        if (!capabilities.Supports("clusters", "remove"))
            throw new RasGateCapabilityNotSupportedException(
                rasGate.Id,
                "clusters",
                "remove");

        await client.RemoveClusterAsync(
            task.ClusterId,
            task.ClusterUser,
            task.ClusterPassword,
            cancellationToken);
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await publisher.TryRemoveClusterAsync(
                rasGate.Id,
                configurationRevision,
                task.ClusterId,
                observedAt,
                cancellationToken))
            throw new RasGateConfigurationChangedException(rasGate.Id);
    }
}