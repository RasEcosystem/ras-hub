using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Models.Search;
using RasHub.Contracts.RasHub.Requests.Search;
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

    private static readonly Func<RasInfobase, InfobaseModel> ModelMapper =
        ModelProjection.Compile();

    public async Task<PageResult<InfobaseModel>> GetPagedAsync(
        Guid rasEndpointId,
        Guid clusterId,
        PageRequest request,
        CancellationToken cancellationToken)
    {
        var query = GetClusterInfobases(rasEndpointId, clusterId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(infobase => infobase.Name)
            .ThenBy(infobase => infobase.ExternalId)
            .ApplyPagination(request.Page, request.PageSize)
            .Select(ModelProjection)
            .ToListAsync(cancellationToken);

        return new PageResult<InfobaseModel>
        {
            Items = items, TotalCount = totalCount, Page = request.Page, PageSize = request.PageSize
        };
    }

    public async Task<IReadOnlyList<InfobaseModel>> GetAllAsync(
        Guid rasEndpointId,
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        return await GetClusterInfobases(rasEndpointId, clusterId)
            .OrderBy(infobase => infobase.Name)
            .ThenBy(infobase => infobase.ExternalId)
            .Select(ModelProjection)
            .ToListAsync(cancellationToken);
    }

    public async Task<PageResult<InfobaseSearchResultModel>> SearchPagedAsync(
        SearchInfobasesRequest search,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var query = CreateSearchQuery(search);
        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .ApplyPagination(page.Page, page.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<InfobaseSearchResultModel>
        {
            Items = rows.Select(ToSearchResult).ToList(),
            TotalCount = totalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
    }

    public async Task<IReadOnlyList<InfobaseSearchResultModel>> SearchAllAsync(
        SearchInfobasesRequest search,
        CancellationToken cancellationToken)
    {
        var rows = await CreateSearchQuery(search)
            .ToListAsync(cancellationToken);

        return rows.Select(ToSearchResult).ToList();
    }

    public Task<InfobaseModel?> GetByExternalIdAsync(
        Guid rasEndpointId,
        Guid clusterId,
        Guid infobaseId,
        CancellationToken cancellationToken)
    {
        return GetClusterInfobases(rasEndpointId, clusterId)
            .Where(infobase => infobase.ExternalId == infobaseId)
            .Select(ModelProjection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private IQueryable<RasInfobase> GetClusterInfobases(
        Guid rasEndpointId,
        Guid clusterId)
    {
        return from infobase in db.RasInfobases.AsNoTracking()
            join cluster in db.RasClusters.AsNoTracking()
                on infobase.RasClusterId equals cluster.Id
            where cluster.RasEndpointId == rasEndpointId &&
                  cluster.ExternalId == clusterId
            select infobase;
    }

    private IQueryable<InfobaseSearchRow> CreateSearchQuery(
        SearchInfobasesRequest search)
    {
        var term = search.Query.Trim().ToUpperInvariant();
        var fields = search.Fields is { Length: > 0 }
            ? search.Fields.ToHashSet()
            : [InfobaseSearchField.Name];
        var searchName = fields.Contains(InfobaseSearchField.Name);
        var searchDescription = fields.Contains(
            InfobaseSearchField.Description);

        return from infobase in db.RasInfobases.AsNoTracking()
            join cluster in db.RasClusters.AsNoTracking()
                on infobase.RasClusterId equals cluster.Id
            join endpoint in db.RasEndpoints.AsNoTracking()
                on cluster.RasEndpointId equals endpoint.Id
            where (search.RasEndpointId == null ||
                   endpoint.Id == search.RasEndpointId) &&
                  (search.ClusterId == null ||
                   cluster.ExternalId == search.ClusterId) &&
                  ((searchName &&
                    infobase.Name.ToUpper().Contains(term)) ||
                   (searchDescription &&
                    infobase.Description.ToUpper().Contains(term)))
            orderby infobase.Name,
                endpoint.Id,
                cluster.ExternalId,
                infobase.ExternalId
            select new InfobaseSearchRow(
                endpoint.Id,
                endpoint.Name,
                cluster.ExternalId,
                cluster.Name,
                infobase);
    }

    private static InfobaseSearchResultModel ToSearchResult(
        InfobaseSearchRow row)
    {
        return new InfobaseSearchResultModel
        {
            RasEndpointId = row.RasEndpointId,
            RasEndpointName = row.RasEndpointName,
            ClusterId = row.ClusterId,
            ClusterName = row.ClusterName,
            Infobase = ModelMapper(row.Infobase)
        };
    }

    private sealed record InfobaseSearchRow(
        Guid RasEndpointId,
        string RasEndpointName,
        Guid ClusterId,
        string ClusterName,
        RasInfobase Infobase);
}
