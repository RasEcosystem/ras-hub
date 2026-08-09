using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Domain;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Exceptions;

namespace RasHub.Application.RasGates.Tasks;

public sealed class SynchronizeClustersTaskHandler(
    IRepository<RasGate> rasGateRepository,
    IRasClusterSnapshotStore snapshotStore,
    IUnitOfWork unitOfWork,
    IRasGateClientFactory clientFactory,
    TimeProvider timeProvider)
    : IBackgroundTaskHandler<SynchronizeClustersTask>
{
    public async Task ExecuteAsync(
        SynchronizeClustersTask task,
        CancellationToken cancellationToken)
    {
        var rasGate = await rasGateRepository.GetByIdAsync(
            task.RasGateId,
            cancellationToken);

        if (rasGate is null)
            throw new NonRetryableBackgroundTaskException(
                $"RasGate '{task.RasGateId}' was not found.");

        var client = clientFactory.Create(rasGate);
        var snapshot = await client.GetClustersAsync(cancellationToken);
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        await snapshotStore.ApplyAsync(
            rasGate.Id,
            snapshot,
            observedAt,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
