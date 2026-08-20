using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed class RemoveClusterTaskHandler(
    IRepository<RasGate> rasGateRepository,
    IRasGateSyncPublisher publisher,
    IRasClusterGateway clusterGateway,
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
            throw new RasGateNotFoundException(task.RasGateId);

        if (!rasGate.IsActive)
            throw new RasGateInactiveException(rasGate.Id);

        var configurationRevision = rasGate.ConfigurationRevision;
        var capabilities = await clusterGateway.GetCapabilitiesAsync(
            rasGate,
            cancellationToken);

        if (!capabilities.Supports("clusters", "remove"))
            throw new RasGateCapabilityNotSupportedException(
                rasGate.Id,
                "clusters",
                "remove");

        await clusterGateway.RemoveClusterAsync(
            rasGate,
            task.ClusterId,
            task.ClusterUser,
            task.ClusterPassword,
            cancellationToken);
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        bool published;

        try
        {
            published = await publisher.TryRemoveClusterAsync(
                rasGate.Id,
                configurationRevision,
                task.ClusterId,
                observedAt,
                cancellationToken);
        }
        catch (Exception exception)
        {
            throw new RasGateMutationPublicationNotConfirmedException(
                rasGate.Id,
                "clusters",
                "remove",
                task.ClusterId,
                exception);
        }

        if (!published)
            throw new RasGateMutationPublicationNotConfirmedException(
                rasGate.Id,
                "clusters",
                "remove",
                task.ClusterId);
    }
}
