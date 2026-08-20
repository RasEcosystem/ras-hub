using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database;

public sealed class RasGateSyncPublisher(
    RasHubDbContext db,
    IRasClusterSnapshotStore clusterSnapshotStore,
    IRasInfobaseSnapshotStore infobaseSnapshotStore)
    : IRasGateSyncPublisher
{
    public async Task<bool> TryPublishStatusAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        RasGateStatus status,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var rasGate = await GetTrackedGateAsync(rasGateId, cancellationToken);

        if (rasGate is null)
            return false;

        PreparePublicationGuard(rasGate, expectedConfigurationRevision);
        rasGate.InstanceName = status.InstanceName;
        rasGate.Version = status.Version;
        rasGate.StatusObservedAt = observedAt;
        rasGate.RacAvailable = status.RacAvailable;
        rasGate.RacVersion = status.RacVersion;
        rasGate.RacStatusObservedAt = observedAt;
        rasGate.LastSeenAt = observedAt;

        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> TryPublishClustersAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        IReadOnlyList<RasClusterSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var rasGate = await GetTrackedGateAsync(rasGateId, cancellationToken);

        if (rasGate is null)
            return false;

        PreparePublicationGuard(rasGate, expectedConfigurationRevision);
        await clusterSnapshotStore.ApplyAsync(
            rasGateId,
            snapshot,
            observedAt,
            cancellationToken);
        rasGate.LastSeenAt = observedAt;

        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> TryPublishClusterAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        RasClusterSnapshot snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var rasGate = await GetTrackedGateAsync(rasGateId, cancellationToken);

        if (rasGate is null)
            return false;

        PreparePublicationGuard(rasGate, expectedConfigurationRevision);
        await clusterSnapshotStore.UpsertAsync(
            rasGateId,
            snapshot,
            observedAt,
            cancellationToken);
        rasGate.LastSeenAt = observedAt;

        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> TryPublishInfobasesAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        Guid clusterId,
        IReadOnlyList<RasInfobaseSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var rasGate = await GetTrackedGateAsync(rasGateId, cancellationToken);

        if (rasGate is null)
            return false;

        var rasClusterId = await GetActiveClusterIdAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        if (rasClusterId is null)
            return false;

        PreparePublicationGuard(rasGate, expectedConfigurationRevision);
        await infobaseSnapshotStore.ApplyAsync(
            rasClusterId.Value,
            snapshot,
            observedAt,
            cancellationToken);
        rasGate.LastSeenAt = observedAt;

        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> TryPublishInfobaseAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        Guid clusterId,
        RasInfobaseSnapshot snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var rasGate = await GetTrackedGateAsync(rasGateId, cancellationToken);

        if (rasGate is null)
            return false;

        var rasClusterId = await GetActiveClusterIdAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        if (rasClusterId is null)
            return false;

        PreparePublicationGuard(rasGate, expectedConfigurationRevision);
        await infobaseSnapshotStore.UpsertAsync(
            rasClusterId.Value,
            snapshot,
            observedAt,
            cancellationToken);
        rasGate.LastSeenAt = observedAt;

        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> TryRemoveClusterAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        Guid clusterId,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var rasGate = await GetTrackedGateAsync(rasGateId, cancellationToken);

        if (rasGate is null)
            return false;

        PreparePublicationGuard(rasGate, expectedConfigurationRevision);
        await clusterSnapshotStore.RemoveAsync(
            rasGateId,
            clusterId,
            cancellationToken);
        rasGate.LastSeenAt = observedAt;

        return await TrySaveAsync(cancellationToken);
    }

    private async Task<RasGate?> GetTrackedGateAsync(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        return db.RasGates.Local.SingleOrDefault(item => item.Id == rasGateId) ??
               await db.RasGates
                   .IgnoreQueryFilters()
                   .SingleOrDefaultAsync(
                       item => item.Id == rasGateId,
                       cancellationToken);
    }

    private async Task<Guid?> GetActiveClusterIdAsync(
        Guid rasGateId,
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        var localCluster = db.RasClusters.Local.SingleOrDefault(item =>
            item.RasGateId == rasGateId &&
            item.ExternalId == clusterId &&
            !item.IsDeleted);

        if (localCluster is not null)
            return localCluster.Id;

        return await db.RasClusters
            .Where(item => item.RasGateId == rasGateId &&
                           item.ExternalId == clusterId)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private void PreparePublicationGuard(
        RasGate rasGate,
        long expectedConfigurationRevision)
    {
        var entry = db.Entry(rasGate);
        entry.Property(item => item.ConfigurationRevision).OriginalValue =
            expectedConfigurationRevision;
        entry.Property(item => item.IsActive).OriginalValue = true;
        entry.Property(item => item.IsDeleted).OriginalValue = false;
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException exception)
            when (exception.Entries.Any(entry => entry.Entity is RasGate))
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}