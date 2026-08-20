using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Tasks.Status;

public sealed class CheckRasGateStatusTaskHandler(
    IRepository<RasGate> repository,
    IRasGateSyncPublisher publisher,
    IRasGateStatusGateway statusGateway,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<CheckRasGateStatusTask>
{
    public async Task ExecuteAsync(
        CheckRasGateStatusTask task,
        CancellationToken cancellationToken)
    {
        var rasGate = await repository.GetByIdAsync(
            task.RasGateId,
            cancellationToken);

        if (rasGate is null)
            throw new RasGateNotFoundException(task.RasGateId);

        if (!rasGate.IsActive)
            throw new RasGateInactiveException(rasGate.Id);

        var configurationRevision = rasGate.ConfigurationRevision;
        var status = await statusGateway.GetStatusAsync(
            rasGate,
            cancellationToken);

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await publisher.TryPublishStatusAsync(
                rasGate.Id,
                configurationRevision,
                status,
                observedAt,
                cancellationToken))
            throw new RasGateConfigurationChangedException(rasGate.Id);
    }
}