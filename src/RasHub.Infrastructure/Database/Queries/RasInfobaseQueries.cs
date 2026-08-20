using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Domain;
using RasHub.Infrastructure.Extensions;

namespace RasHub.Infrastructure.Database.Queries;

public sealed class RasInfobaseQueries(RasHubDbContext db)
{
    private static readonly Expression<Func<RasInfobase, InfobaseModel>>
        ModelProjection = infobase => new InfobaseModel
        {
            Id = infobase.ExternalId,
            Name = infobase.Name,
            Description = infobase.Description,
            ObservedAt = infobase.ObservedAt
        };

    public async Task<PageResult<InfobaseModel>> GetPagedAsync(
        Guid rasGateId,
        Guid clusterId,
        PageRequest request,
        CancellationToken cancellationToken)
    {
        var query = GetClusterInfobases(rasGateId, clusterId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(infobase => infobase.Name)
            .ThenBy(infobase => infobase.ExternalId)
            .ApplyPagination(request.Page, request.PageSize)
            .Select(ModelProjection)
            .ToListAsync(cancellationToken);

        return new PageResult<InfobaseModel>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    public Task<InfobaseModel?> GetByExternalIdAsync(
        Guid rasGateId,
        Guid clusterId,
        Guid infobaseId,
        CancellationToken cancellationToken)
    {
        return GetClusterInfobases(rasGateId, clusterId)
            .Where(infobase => infobase.ExternalId == infobaseId)
            .Select(ModelProjection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private IQueryable<RasInfobase> GetClusterInfobases(
        Guid rasGateId,
        Guid clusterId)
    {
        return from infobase in db.RasInfobases.AsNoTracking()
               join cluster in db.RasClusters.AsNoTracking()
                   on infobase.RasClusterId equals cluster.Id
               where cluster.RasGateId == rasGateId &&
                     cluster.ExternalId == clusterId
               select infobase;
    }
}