using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed class UpdateClusterTaskHandler(
    IRepository<RasGate> rasGateRepository,
    IRasGateSyncPublisher publisher,
    IRasClusterGateway clusterGateway,
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
            throw new RasGateNotFoundException(task.RasGateId);

        if (!rasGate.IsActive)
            throw new RasGateInactiveException(rasGate.Id);

        var configurationRevision = rasGate.ConfigurationRevision;
        var capabilities = await clusterGateway.GetCapabilitiesAsync(
            rasGate,
            cancellationToken);

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

        await clusterGateway.UpdateClusterAsync(
            rasGate,
            task.ClusterId,
            task.Options,
            cancellationToken);
        RasClusterSnapshot snapshot;

        try
        {
            snapshot = await clusterGateway.GetClusterAsync(
                rasGate,
                task.ClusterId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            throw new RasGateMutationReadBackNotConfirmedException(
                rasGate.Id,
                "clusters",
                "update",
                task.ClusterId,
                exception);
        }

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        bool published;

        try
        {
            published = await publisher.TryPublishClusterAsync(
                rasGate.Id,
                configurationRevision,
                snapshot,
                observedAt,
                cancellationToken);
        }
        catch (Exception exception)
        {
            throw new RasGateMutationPublicationNotConfirmedException(
                rasGate.Id,
                "clusters",
                "update",
                task.ClusterId,
                exception);
        }

        if (!published)
            throw new RasGateMutationPublicationNotConfirmedException(
                rasGate.Id,
                "clusters",
                "update",
                task.ClusterId);
    }
}