using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Models.Search;
using RasHub.Contracts.RasHub.Requests.Search;
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

    private static readonly Func<RasCluster, ClusterModel> ModelMapper =
        ModelProjection.Compile();

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

    public async Task<IReadOnlyList<ClusterModel>> GetAllAsync(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        return await db.RasClusters
            .AsNoTracking()
            .Where(cluster => cluster.RasGateId == rasGateId)
            .OrderBy(cluster => cluster.Name)
            .ThenBy(cluster => cluster.ExternalId)
            .Select(ModelProjection)
            .ToListAsync(cancellationToken);
    }

    public async Task<PageResult<ClusterSearchResultModel>> SearchPagedAsync(
        SearchClustersRequest search,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var query = CreateSearchQuery(search);
        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .ApplyPagination(page.Page, page.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<ClusterSearchResultModel>
        {
            Items = rows.Select(ToSearchResult).ToList(),
            TotalCount = totalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
    }

    public async Task<IReadOnlyList<ClusterSearchResultModel>> SearchAllAsync(
        SearchClustersRequest search,
        CancellationToken cancellationToken)
    {
        var rows = await CreateSearchQuery(search)
            .ToListAsync(cancellationToken);

        return rows.Select(ToSearchResult).ToList();
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

    private static IQueryable<RasCluster> ApplySearch(
        IQueryable<RasCluster> query,
        SearchClustersRequest search)
    {
        if (search.RasGateId is { } rasGateId)
            query = query.Where(cluster => cluster.RasGateId == rasGateId);

        var term = search.Query.Trim().ToUpperInvariant();
        var fields = search.Fields is { Length: > 0 }
            ? search.Fields.ToHashSet()
            : [ClusterSearchField.Name];
        var searchName = fields.Contains(ClusterSearchField.Name);
        var searchHost = fields.Contains(ClusterSearchField.Host);

        return query.Where(cluster =>
            (searchName && cluster.Name.ToUpper().Contains(term)) ||
            (searchHost && cluster.Host.ToUpper().Contains(term)));
    }

    private IQueryable<ClusterSearchRow> CreateSearchQuery(
        SearchClustersRequest search)
    {
        var clusters = ApplySearch(
            db.RasClusters.AsNoTracking(),
            search);

        return from cluster in clusters
               join rasGate in db.RasGates.AsNoTracking()
                   on cluster.RasGateId equals rasGate.Id
               orderby cluster.Name, rasGate.Id, cluster.ExternalId
               select new ClusterSearchRow(
                   rasGate.Id,
                   rasGate.Name,
                   cluster);
    }

    private static ClusterSearchResultModel ToSearchResult(
        ClusterSearchRow row)
    {
        return new ClusterSearchResultModel
        {
            RasGateId = row.RasGateId,
            RasGateName = row.RasGateName,
            Cluster = ModelMapper(row.Cluster)
        };
    }

    private sealed record ClusterSearchRow(
        Guid RasGateId,
        string RasGateName,
        RasCluster Cluster);
}
