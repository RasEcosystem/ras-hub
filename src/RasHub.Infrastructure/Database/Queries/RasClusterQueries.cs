using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Domain;
using RasHub.Infrastructure.Extensions;
using ContractLoadBalancingMode = RasHub.Contracts.RasHub.Models.RasClusterLoadBalancingMode;
using DomainLoadBalancingMode = RasHub.Domain.Enums.RasClusterLoadBalancingMode;

namespace RasHub.Infrastructure.Database.Queries;

public sealed class RasClusterQueries(RasHubDbContext db)
{
    private static readonly Expression<Func<RasCluster, RasClusterModel>>
        ModelProjection = cluster => new RasClusterModel(
            cluster.ExternalId,
            cluster.Name,
            cluster.Host,
            cluster.Port,
            cluster.ExpirationTimeoutSeconds,
            cluster.LifetimeLimitSeconds,
            cluster.MaxMemorySizeKb,
            cluster.MaxMemoryTimeLimitSeconds,
            cluster.SecurityLevel,
            cluster.SessionFaultToleranceLevel,
            cluster.LoadBalancingMode == DomainLoadBalancingMode.Performance
                ? ContractLoadBalancingMode.Performance
                : ContractLoadBalancingMode.Memory,
            cluster.ErrorsCountThresholdPercent,
            cluster.KillProblemProcesses,
            cluster.KillByMemoryWithDump,
            cluster.AllowAccessRightAuditEventsRecording,
            cluster.PingPeriod,
            cluster.PingTimeout,
            cluster.RestartSchedule,
            cluster.ObservedAt);

    public async Task<PageResult<RasClusterModel>> GetPagedAsync(
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

        return new PageResult<RasClusterModel>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    public Task<RasClusterModel?> GetByExternalIdAsync(
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