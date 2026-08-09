using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
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

        var client = clientFactory.Create(rasGate);
        var status = await client.GetStatusAsync(cancellationToken);

        rasGate.InstanceName = status.InstanceName;
        rasGate.Version = status.Version;
        rasGate.StatusObservedAt = timeProvider.GetUtcNow().UtcDateTime;

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}