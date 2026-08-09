using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Domain;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Exceptions;

namespace RasHub.Application.RasGates.Tasks;

public sealed class RefreshRasGateStatusTaskHandler(
    IRepository<RasGate> repository,
    IUnitOfWork unitOfWork,
    IRasGateClientFactory clientFactory,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<RefreshRasGateStatusTask>
{
    public async Task ExecuteAsync(
        RefreshRasGateStatusTask task,
        CancellationToken cancellationToken)
    {
        var rasGate = await repository.GetByIdAsync(
            task.RasGateId,
            cancellationToken);

        if (rasGate is null)
            throw new NonRetryableBackgroundTaskException(
                $"RasGate '{task.RasGateId}' was not found.");

        if (!rasGate.IsActive)
            throw new RasGateInactiveException(rasGate.Id);

        var client = clientFactory.Create(rasGate);
        var status = await client.GetStatusAsync(cancellationToken);

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        rasGate.InstanceName = status.InstanceName;
        rasGate.Version = status.Version;
        rasGate.StatusObservedAt = observedAt;
        rasGate.LastSeenAt = observedAt;

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}