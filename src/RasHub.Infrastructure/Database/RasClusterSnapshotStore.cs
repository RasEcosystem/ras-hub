using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database;

public sealed class RasClusterSnapshotStore(RasHubDbContext db)
    : IRasClusterSnapshotStore
{
    public async Task ApplyAsync(
        Guid rasGateId,
        IReadOnlyList<RasClusterSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var storedClusters = await db.RasClusters
            .IgnoreQueryFilters()
            .Where(cluster => cluster.RasGateId == rasGateId)
            .ToListAsync(cancellationToken);
        var storedByExternalId = storedClusters.ToDictionary(cluster => cluster.ExternalId);
        var observedExternalIds = new HashSet<Guid>();

        foreach (var item in snapshot)
        {
            if (!observedExternalIds.Add(item.ExternalId))
                throw new InvalidOperationException(
                    $"Cluster snapshot contains duplicate external ID '{item.ExternalId}'.");

            if (!storedByExternalId.TryGetValue(item.ExternalId, out var cluster))
            {
                cluster = new RasCluster
                {
                    RasGateId = rasGateId,
                    ExternalId = item.ExternalId,
                    Name = item.Name,
                    Host = item.Host
                };
                db.RasClusters.Add(cluster);
            }

            Apply(cluster, item, observedAt);
        }

        var removedClusters = storedClusters
            .Where(cluster =>
                !cluster.IsDeleted &&
                !observedExternalIds.Contains(cluster.ExternalId))
            .ToArray();

        await InvalidateInfobasesAsync(
            removedClusters.Select(cluster => cluster.Id),
            cancellationToken);
        db.RasClusters.RemoveRange(removedClusters);
    }

    public async Task InvalidateAsync(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var clusters = await db.RasClusters
            .Where(cluster => cluster.RasGateId == rasGateId)
            .ToListAsync(cancellationToken);

        await InvalidateInfobasesAsync(
            clusters.Select(cluster => cluster.Id),
            cancellationToken);
        db.RasClusters.RemoveRange(clusters);
    }

    public async Task UpsertAsync(
        Guid rasGateId,
        RasClusterSnapshot snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var cluster = await db.RasClusters
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.RasGateId == rasGateId &&
                        item.ExternalId == snapshot.ExternalId,
                cancellationToken);

        if (cluster is null)
        {
            cluster = new RasCluster
            {
                RasGateId = rasGateId,
                ExternalId = snapshot.ExternalId,
                Name = snapshot.Name,
                Host = snapshot.Host
            };
            db.RasClusters.Add(cluster);
        }

        Apply(cluster, snapshot, observedAt);
    }

    public async Task RemoveAsync(
        Guid rasGateId,
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        var cluster = await db.RasClusters
            .SingleOrDefaultAsync(
                item => item.RasGateId == rasGateId &&
                        item.ExternalId == clusterId,
                cancellationToken);

        if (cluster is not null)
        {
            await InvalidateInfobasesAsync(
                [cluster.Id],
                cancellationToken);
            db.RasClusters.Remove(cluster);
        }
    }

    private async Task InvalidateInfobasesAsync(
        IEnumerable<Guid> rasClusterIds,
        CancellationToken cancellationToken)
    {
        var clusterIds = rasClusterIds.Distinct().ToArray();

        if (clusterIds.Length == 0)
            return;

        var infobases = await db.RasInfobases
            .Where(infobase => clusterIds.Contains(infobase.RasClusterId))
            .ToListAsync(cancellationToken);

        db.RasInfobases.RemoveRange(infobases);
    }

    private static void Apply(
        RasCluster cluster,
        RasClusterSnapshot snapshot,
        DateTime observedAt)
    {
        cluster.Name = snapshot.Name;
        cluster.Host = snapshot.Host;
        cluster.Port = snapshot.Port;
        cluster.ExpirationTimeoutSeconds = snapshot.ExpirationTimeoutSeconds;
        cluster.LifetimeLimitSeconds = snapshot.LifetimeLimitSeconds;
        cluster.MaxMemorySizeKb = snapshot.MaxMemorySizeKb;
        cluster.MaxMemoryTimeLimitSeconds = snapshot.MaxMemoryTimeLimitSeconds;
        cluster.SecurityLevel = snapshot.SecurityLevel;
        cluster.SessionFaultToleranceLevel = snapshot.SessionFaultToleranceLevel;
        cluster.LoadBalancingMode = snapshot.LoadBalancingMode;
        cluster.ErrorsCountThresholdPercent = snapshot.ErrorsCountThresholdPercent;
        cluster.KillProblemProcesses = snapshot.KillProblemProcesses;
        cluster.KillByMemoryWithDump = snapshot.KillByMemoryWithDump;
        cluster.AllowAccessRightAuditEventsRecording =
            snapshot.AllowAccessRightAuditEventsRecording;
        cluster.PingPeriod = snapshot.PingPeriod;
        cluster.PingTimeout = snapshot.PingTimeout;
        cluster.RestartSchedule = snapshot.RestartSchedule;
        cluster.ObservedAt = observedAt;
        cluster.IsDeleted = false;
        cluster.DeletedAt = null;
    }
}