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
            RasGateId = endpoint.RasGateId,
            Name = endpoint.Name,
            Host = endpoint.Host,
            Port = endpoint.Port,
            IsActive = endpoint.IsActive,
            LastSeenAt = endpoint.LastSeenAt,
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
        var gates = db.RasGates.IgnoreQueryFilters();

        return query
            .Join(
                gates,
                endpoint => endpoint.RasGateId,
                gate => gate.Id,
                (endpoint, gate) => new { Endpoint = endpoint, Gate = gate })
            .AsNoTracking()
            .OrderBy(item => item.Endpoint.IsDeleted)
            .ThenBy(item => item.Endpoint.Name)
            .ThenBy(item => item.Endpoint.Id)
            .Select(item => new RasEndpointAdministrationItem
            {
                Id = item.Endpoint.Id,
                Name = item.Endpoint.Name,
                RasGateId = item.Endpoint.RasGateId,
                RasGateName = item.Gate.Name,
                RasGateUrl = item.Gate.Url,
                RasGatePort = item.Gate.Port,
                RasGateIsActive = item.Gate.IsActive,
                RasGateIsDeleted = item.Gate.IsDeleted,
                Host = item.Endpoint.Host,
                Port = item.Endpoint.Port,
                IsActive = item.Endpoint.IsActive,
                ConfigurationRevision = item.Endpoint.ConfigurationRevision,
                CreatedAt = item.Endpoint.CreatedAt,
                UpdatedAt = item.Endpoint.UpdatedAt,
                IsDeleted = item.Endpoint.IsDeleted,
                DeletedAt = item.Endpoint.DeletedAt
            })
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
            Items = items, TotalCount = totalCount, Page = request.Page, PageSize = request.PageSize
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

    public Task<RasEndpointActivity?> GetActivityAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return db.RasEndpoints
            .AsNoTracking()
            .Where(endpoint => endpoint.Id == id)
            .Select(endpoint => new RasEndpointActivity(endpoint.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }
}

public sealed record RasEndpointActivity(bool IsActive);

public sealed record RasEndpointAdministrationItem
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required Guid RasGateId { get; init; }

    public required string RasGateName { get; init; }

    public required string RasGateUrl { get; init; }

    public required int RasGatePort { get; init; }

    public required bool RasGateIsActive { get; init; }

    public required bool RasGateIsDeleted { get; init; }

    public required string Host { get; init; }

    public required int Port { get; init; }

    public required bool IsActive { get; init; }

    public required long ConfigurationRevision { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; init; }

    public required bool IsDeleted { get; init; }

    public required DateTime? DeletedAt { get; init; }
}
