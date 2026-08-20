using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Domain;
using RasHub.Infrastructure.Extensions;
using ContractLoadBalancingMode = RasHub.Contracts.RasHub.Models.ClusterLoadBalancingMode;
using DomainLoadBalancingMode = RasHub.Domain.Enums.RasClusterLoadBalancingMode;

namespace RasHub.Infrastructure.Database.Queries;

public sealed class RasClusterQueries(RasHubDbContext db)
{
    private static readonly Expression<Func<RasCluster, ClusterModel>>
        ModelProjection = cluster => new ClusterModel
        {
            Id = cluster.ExternalId,
            Name = cluster.Name,
            Host = cluster.Host,
            Port = cluster.Port,
            ExpirationTimeoutSeconds = cluster.ExpirationTimeoutSeconds,
            LifetimeLimitSeconds = cluster.LifetimeLimitSeconds,
            MaxMemorySizeKb = cluster.MaxMemorySizeKb,
            MaxMemoryTimeLimitSeconds = cluster.MaxMemoryTimeLimitSeconds,
            SecurityLevel = cluster.SecurityLevel,
            SessionFaultToleranceLevel = cluster.SessionFaultToleranceLevel,
            LoadBalancingMode = cluster.LoadBalancingMode == DomainLoadBalancingMode.Performance
                ? ContractLoadBalancingMode.Performance
                : ContractLoadBalancingMode.Memory,
            ErrorsCountThresholdPercent = cluster.ErrorsCountThresholdPercent,
            KillProblemProcesses = cluster.KillProblemProcesses,
            KillByMemoryWithDump = cluster.KillByMemoryWithDump,
            AllowAccessRightAuditEventsRecording =
                cluster.AllowAccessRightAuditEventsRecording,
            PingPeriod = cluster.PingPeriod,
            PingTimeout = cluster.PingTimeout,
            RestartSchedule = cluster.RestartSchedule,
            ObservedAt = cluster.ObservedAt
        };

    public async Task<PageResult<ClusterModel>> GetPagedAsync(
        Guid rasGateId,
        PageRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.RasClusters
            .AsNoTracking()
            .Where(cluster => cluster.RasGateId == rasGateId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(cluster => cluster.Name)
            .ThenBy(cluster => cluster.ExternalId)
            .ApplyPagination(request.Page, request.PageSize)
            .Select(ModelProjection)
            .ToListAsync(cancellationToken);

        return new PageResult<ClusterModel>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    public Task<ClusterModel?> GetByExternalIdAsync(
        Guid rasGateId,
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        return db.RasClusters
            .AsNoTracking()
            .Where(cluster => cluster.RasGateId == rasGateId &&
                              cluster.ExternalId == clusterId)
            .Select(ModelProjection)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
