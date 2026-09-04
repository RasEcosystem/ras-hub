using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database;

public sealed class RasEndpointSyncPublisher(
    RasHubDbContext db,
    IRasClusterSnapshotStore clusterSnapshotStore,
    IRasInfobaseSnapshotStore infobaseSnapshotStore)
    : IRasEndpointSyncPublisher
{
    public async Task<bool> TryPublishClustersAsync(
        RasEndpointExecutionGuard guard,
        IReadOnlyList<RasClusterSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var target = await GetTrackedTargetAsync(guard, cancellationToken);

        if (target is null)
            return false;

        PreparePublicationGuards(target.Value, guard);
        await clusterSnapshotStore.ApplyAsync(
            guard.RasEndpointId,
            snapshot,
            observedAt,
            cancellationToken);
        Observe(target.Value.Endpoint, observedAt);

        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> TryPublishClusterAsync(
        RasEndpointExecutionGuard guard,
        RasClusterSnapshot snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var target = await GetTrackedTargetAsync(guard, cancellationToken);

        if (target is null)
            return false;

        PreparePublicationGuards(target.Value, guard);
        await clusterSnapshotStore.UpsertAsync(
            guard.RasEndpointId,
            snapshot,
            observedAt,
            cancellationToken);
        Observe(target.Value.Endpoint, observedAt);

        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> TryPublishInfobasesAsync(
        RasEndpointExecutionGuard guard,
        Guid clusterId,
        IReadOnlyList<RasInfobaseSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var target = await GetTrackedTargetAsync(guard, cancellationToken);

        if (target is null)
            return false;

        var rasClusterId = await GetActiveClusterIdAsync(
            guard.RasEndpointId,
            clusterId,
            cancellationToken);

        if (rasClusterId is null)
            return false;

        PreparePublicationGuards(target.Value, guard);
        await infobaseSnapshotStore.ApplyAsync(
            rasClusterId.Value,
            snapshot,
            observedAt,
            cancellationToken);
        Observe(target.Value.Endpoint, observedAt);

        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> TryPublishInfobaseAsync(
        RasEndpointExecutionGuard guard,
        Guid clusterId,
        RasInfobaseSnapshot snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var target = await GetTrackedTargetAsync(guard, cancellationToken);

        if (target is null)
            return false;

        var rasClusterId = await GetActiveClusterIdAsync(
            guard.RasEndpointId,
            clusterId,
            cancellationToken);

        if (rasClusterId is null)
            return false;

        PreparePublicationGuards(target.Value, guard);
        await infobaseSnapshotStore.UpsertAsync(
            rasClusterId.Value,
            snapshot,
            observedAt,
            cancellationToken);
        Observe(target.Value.Endpoint, observedAt);

        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> TryRemoveClusterAsync(
        RasEndpointExecutionGuard guard,
        Guid clusterId,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var target = await GetTrackedTargetAsync(guard, cancellationToken);

        if (target is null)
            return false;

        PreparePublicationGuards(target.Value, guard);
        await clusterSnapshotStore.RemoveAsync(
            guard.RasEndpointId,
            clusterId,
            cancellationToken);
        Observe(target.Value.Endpoint, observedAt);

        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> TryRemoveInfobaseAsync(
        RasEndpointExecutionGuard guard,
        Guid clusterId,
        Guid infobaseId,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var target = await GetTrackedTargetAsync(guard, cancellationToken);

        if (target is null)
            return false;

        var rasClusterId = await GetActiveClusterIdAsync(
            guard.RasEndpointId,
            clusterId,
            cancellationToken);

        if (rasClusterId is null)
            return false;

        PreparePublicationGuards(target.Value, guard);
        await infobaseSnapshotStore.RemoveAsync(
            rasClusterId.Value,
            infobaseId,
            cancellationToken);
        Observe(target.Value.Endpoint, observedAt);

        return await TrySaveAsync(cancellationToken);
    }

    private async Task<(RasEndpoint Endpoint, RasGate Gate)?>
        GetTrackedTargetAsync(
            RasEndpointExecutionGuard guard,
            CancellationToken cancellationToken)
    {
        var endpoint = db.RasEndpoints.Local.SingleOrDefault(
                           item => item.Id == guard.RasEndpointId) ??
                       await db.RasEndpoints
                           .IgnoreQueryFilters()
                           .SingleOrDefaultAsync(
                               item => item.Id == guard.RasEndpointId,
                               cancellationToken);

        if (endpoint is null)
            return null;

        var gate = db.RasGates.Local.SingleOrDefault(
                       item => item.Id == guard.RasGateId) ??
                   await db.RasGates
                       .IgnoreQueryFilters()
                       .SingleOrDefaultAsync(
                           item => item.Id == guard.RasGateId,
                           cancellationToken);

        return gate is null ? null : (endpoint, gate);
    }

    private async Task<Guid?> GetActiveClusterIdAsync(
        Guid rasEndpointId,
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        var localCluster = db.RasClusters.Local.SingleOrDefault(item =>
            item.RasEndpointId == rasEndpointId &&
            item.ExternalId == clusterId &&
            !item.IsDeleted);

        if (localCluster is not null)
            return localCluster.Id;

        return await db.RasClusters
            .Where(item => item.RasEndpointId == rasEndpointId &&
                           item.ExternalId == clusterId)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private void PreparePublicationGuards(
        (RasEndpoint Endpoint, RasGate Gate) target,
        RasEndpointExecutionGuard guard)
    {
        var endpointEntry = db.Entry(target.Endpoint);
        endpointEntry.Property(item => item.ConfigurationRevision)
            .OriginalValue = guard.RasEndpointConfigurationRevision;
        endpointEntry.Property(item => item.IsActive).OriginalValue = true;
        endpointEntry.Property(item => item.IsDeleted).OriginalValue = false;

        var gateEntry = db.Entry(target.Gate);
        gateEntry.Property(item => item.ConfigurationRevision).OriginalValue =
            guard.RasGateConfigurationRevision;
        gateEntry.Property(item => item.IsActive).OriginalValue = true;
        gateEntry.Property(item => item.IsDeleted).OriginalValue = false;

        // Keep the Gate guard part of the same atomic publication without
        // claiming ownership of Gate observation fields.
        gateEntry.Property(item => item.ConfigurationRevision).IsModified = true;
    }

    private static void Observe(RasEndpoint endpoint, DateTime observedAt)
    {
        endpoint.LastSeenAt = observedAt;
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException exception)
            when (exception.Entries.Any(entry =>
                entry.Entity is RasEndpoint or RasGate))
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
