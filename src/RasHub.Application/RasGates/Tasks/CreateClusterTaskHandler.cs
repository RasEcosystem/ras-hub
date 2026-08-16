using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Tasks;

public sealed class CreateClusterTaskHandler(
    IRepository<RasGate> rasGateRepository,
    IRasGateSyncPublisher publisher,
    IRasGateClientFactory clientFactory,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<CreateClusterTask, Guid>
{
    public async Task<Guid> ExecuteAsync(
        CreateClusterTask task,
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

        if (!capabilities.Supports("clusters", "insert"))
            throw new RasGateCapabilityNotSupportedException(
                rasGate.Id,
                "clusters",
                "insert");

        if (!capabilities.Supports("clusters", "info"))
            throw new RasGateCapabilityNotSupportedException(
                rasGate.Id,
                "clusters",
                "info");

        var clusterId = await client.CreateClusterAsync(
            task.Options,
            cancellationToken);
        var snapshot = await client.GetClusterAsync(
            clusterId,
            cancellationToken);
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await publisher.TryPublishClusterAsync(
                rasGate.Id,
                configurationRevision,
                snapshot,
                observedAt,
                cancellationToken))
            throw new RasGateConfigurationChangedException(rasGate.Id);

        return clusterId;
    }
}