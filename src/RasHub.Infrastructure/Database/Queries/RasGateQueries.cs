using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Responses;
using RasHub.Domain;
using RasHub.Infrastructure.Extensions;

namespace RasHub.Infrastructure.Database.Queries;

public sealed class RasGateQueries(RasHubDbContext db)
{
    private static readonly Expression<Func<RasGate, RasGateModel>>
        ModelProjection = rasGate => new RasGateModel(
            rasGate.Id,
            rasGate.Name,
            rasGate.Url,
            rasGate.Port,
            rasGate.IsActive,
            rasGate.CreatedAt,
            rasGate.UpdatedAt);

    public Task<List<Guid>> GetActiveIdsAsync(CancellationToken cancellationToken)
    {
        return db.RasGates
            .AsNoTracking()
            .Where(rasGate => rasGate.IsActive)
            .OrderBy(rasGate => rasGate.Id)
            .Select(rasGate => rasGate.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<RasGateHealthSummary> GetHealthSummaryAsync(
        DateTime onlineSince,
        CancellationToken cancellationToken)
    {
        var summary = await db.RasGates
            .AsNoTracking()
            .Where(rasGate => rasGate.IsActive)
            .GroupBy(_ => 1)
            .Select(group => new RasGateHealthSummary(
                group.Count(),
                group.Count(rasGate => rasGate.LastSeenAt >= onlineSince)))
            .SingleOrDefaultAsync(cancellationToken);

        return summary ?? new RasGateHealthSummary(0, 0);
    }

    public async Task<PageResult<RasGateModel>> GetPagedAsync(
        PageRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.RasGates.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(rasGate => rasGate.CreatedAt)
            .ThenBy(rasGate => rasGate.Id)
            .ApplyPagination(
                request.Page,
                request.PageSize)
            .Select(ModelProjection)
            .ToListAsync(cancellationToken);

        return new PageResult<RasGateModel>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    public Task<RasGateModel?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return db.RasGates
            .AsNoTracking()
            .Where(rasGate => rasGate.Id == id)
            .Select(ModelProjection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<RasGateActivity?> GetActivityAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return db.RasGates
            .AsNoTracking()
            .Where(rasGate => rasGate.Id == id)
            .Select(rasGate => new RasGateActivity(rasGate.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<RasGateStatusResponse?> GetStatusAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return db.RasGates
            .AsNoTracking()
            .Where(rasGate => rasGate.Id == id)
            .Select(rasGate => new RasGateStatusResponse
            {
                InstanceName = rasGate.InstanceName,
                Version = rasGate.Version,
                ObservedAt = rasGate.StatusObservedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
    }
}

public sealed record RasGateHealthSummary(int TotalCount, int OnlineCount);

public sealed record RasGateActivity(bool IsActive);