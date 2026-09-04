using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Domain;
using RasHub.Infrastructure.Extensions;

namespace RasHub.Infrastructure.Database.Queries;

public sealed class RasEndpointQueries(RasHubDbContext db)
{
    private static readonly Expression<Func<RasEndpoint, RasEndpointModel>>
        ModelProjection = endpoint => new RasEndpointModel
        {
            Id = endpoint.Id,
            Name = endpoint.Name,
            Host = endpoint.Host,
            Port = endpoint.Port,
            IsActive = endpoint.IsActive,
            ConfigurationRevision = endpoint.ConfigurationRevision,
            CreatedAt = endpoint.CreatedAt,
            UpdatedAt = endpoint.UpdatedAt
        };

    public Task<List<RasEndpointAdministrationItem>>
        GetAdministrationItemsAsync(
            bool includeDeleted,
            CancellationToken cancellationToken)
    {
        var query = includeDeleted
            ? db.RasEndpoints.IgnoreQueryFilters()
            : db.RasEndpoints;

        return query
            .AsNoTracking()
            .OrderBy(endpoint => endpoint.IsDeleted)
            .ThenBy(endpoint => endpoint.Name)
            .ThenBy(endpoint => endpoint.Id)
            .Select(endpoint => new RasEndpointAdministrationItem(
                endpoint.Id,
                endpoint.Name,
                endpoint.Host,
                endpoint.Port,
                endpoint.IsActive,
                endpoint.ConfigurationRevision,
                endpoint.CreatedAt,
                endpoint.UpdatedAt,
                endpoint.IsDeleted,
                endpoint.DeletedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<PageResult<RasEndpointModel>> GetPagedAsync(
        PageRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.RasEndpoints.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(endpoint => endpoint.CreatedAt)
            .ThenBy(endpoint => endpoint.Id)
            .ApplyPagination(request.Page, request.PageSize)
            .Select(ModelProjection)
            .ToListAsync(cancellationToken);

        return new PageResult<RasEndpointModel>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    public async Task<IReadOnlyList<RasEndpointModel>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        return await db.RasEndpoints
            .AsNoTracking()
            .OrderBy(endpoint => endpoint.Name)
            .ThenBy(endpoint => endpoint.Id)
            .Select(ModelProjection)
            .ToListAsync(cancellationToken);
    }

    public Task<RasEndpointModel?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return db.RasEndpoints
            .AsNoTracking()
            .Where(endpoint => endpoint.Id == id)
            .Select(ModelProjection)
            .SingleOrDefaultAsync(cancellationToken);
    }
}

public sealed record RasEndpointAdministrationItem(
    Guid Id,
    string Name,
    string Host,
    int Port,
    bool IsActive,
    long ConfigurationRevision,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsDeleted,
    DateTime? DeletedAt);
