using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Tasks;

public sealed class UpdateClusterTaskHandler(
    IRepository<RasGate> rasGateRepository,
    IRasGateSyncPublisher publisher,
    IRasGateClientFactory clientFactory,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<UpdateClusterTask>
{
    public async Task ExecuteAsync(
        UpdateClusterTask task,
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

        if (!capabilities.Supports("clusters", "update"))
            throw new RasGateCapabilityNotSupportedException(
                rasGate.Id,
                "clusters",
                "update");

        if (!capabilities.Supports("clusters", "info"))
            throw new RasGateCapabilityNotSupportedException(
                rasGate.Id,
                "clusters",
                "info");

        await client.UpdateClusterAsync(
            task.ClusterId,
            task.Options,
            cancellationToken);
        var snapshot = await client.GetClusterAsync(
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