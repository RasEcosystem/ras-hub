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
        ModelProjection = rasGate => new RasGateModel
        {
            Id = rasGate.Id,
            Name = rasGate.Name,
            Url = rasGate.Url,
            Port = rasGate.Port,
            IsActive = rasGate.IsActive,
            CreatedAt = rasGate.CreatedAt,
            UpdatedAt = rasGate.UpdatedAt
        };

    public Task<List<Guid>> GetActiveIdsAsync(CancellationToken cancellationToken)
    {
        return db.RasGates
            .AsNoTracking()
            .Where(rasGate => rasGate.IsActive)
            .OrderBy(rasGate => rasGate.Id)
            .Select(rasGate => rasGate.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<List<RasGateAdministrationItem>> GetAdministrationItemsAsync(
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var query = includeDeleted
            ? db.RasGates.IgnoreQueryFilters()
            : db.RasGates;

        return query
            .AsNoTracking()
            .OrderBy(rasGate => rasGate.IsDeleted)
            .ThenBy(rasGate => rasGate.Name)
            .ThenBy(rasGate => rasGate.Id)
            .Select(rasGate => new RasGateAdministrationItem(
                rasGate.Id,
                rasGate.Name,
                rasGate.Url,
                rasGate.Port,
                rasGate.IsActive,
                rasGate.InstanceName,
                rasGate.Version,
                rasGate.StatusObservedAt,
                rasGate.RacAvailable,
                rasGate.RacVersion,
                rasGate.RacStatusObservedAt,
                rasGate.LastSeenAt,
                rasGate.CreatedAt,
                rasGate.UpdatedAt,
                rasGate.IsDeleted,
                rasGate.DeletedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<List<RasGateAdministrationItem>> GetAdministrationItemsAsync(
        CancellationToken cancellationToken)
    {
        return GetAdministrationItemsAsync(false, cancellationToken);
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
                group.Count(rasGate =>
                    rasGate.StatusObservedAt >= onlineSince)))
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

    public async Task<RasGateStatusQueryResult?> GetStatusAsync(
        Guid id,
        DateTime onlineSince,
        CancellationToken cancellationToken)
    {
        var observation = await db.RasGates
            .AsNoTracking()
            .Where(rasGate => rasGate.Id == id)
            .Select(rasGate => new
            {
                rasGate.IsActive,
                rasGate.InstanceName,
                rasGate.Version,
                rasGate.StatusObservedAt,
                rasGate.RacAvailable,
                rasGate.RacVersion,
                rasGate.RacStatusObservedAt
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (observation is null)
            return null;

        return new RasGateStatusQueryResult(
            observation.IsActive,
            new RasGateStatusResponse
            {
                State = RasGateHealthStateClassifier.Classify(
                    observation.StatusObservedAt,
                    observation.RacStatusObservedAt,
                    observation.RacAvailable,
                    onlineSince),
                InstanceName = observation.InstanceName,
                RasGateVersion = observation.Version,
                RasGateObservedAt = observation.StatusObservedAt,
                RacAvailable = observation.RacAvailable,
                RacVersion = observation.RacVersion,
                RacObservedAt = observation.RacStatusObservedAt
            });
    }
}

public sealed record RasGateHealthSummary(int TotalCount, int OnlineCount);

public sealed record RasGateActivity(bool IsActive);

public sealed record RasGateStatusQueryResult(
    bool IsActive,
    RasGateStatusResponse Status);

public sealed record RasGateAdministrationItem(
    Guid Id,
    string Name,
    string Url,
    int Port,
    bool IsActive,
    string? InstanceName,
    string? Version,
    DateTime? StatusObservedAt,
    bool? RacAvailable,
    string? RacVersion,
    DateTime? RacStatusObservedAt,
    DateTime? LastSeenAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsDeleted,
    DateTime? DeletedAt)
{
    public RasGateHealthState GetHealthState(DateTime onlineSince)
    {
        return RasGateHealthStateClassifier.Classify(
            StatusObservedAt,
            RacStatusObservedAt,
            RacAvailable,
            onlineSince);
    }
}

internal static class RasGateHealthStateClassifier
{
    public static RasGateHealthState Classify(
        DateTime? rasGateObservedAt,
        DateTime? racObservedAt,
        bool? racAvailable,
        DateTime onlineSince)
    {
        if (rasGateObservedAt is null)
            return RasGateHealthState.Unknown;

        if (rasGateObservedAt < onlineSince)
            return RasGateHealthState.Offline;

        return racObservedAt is null ||
               racObservedAt < onlineSince ||
               racAvailable != true
            ? RasGateHealthState.Degraded
            : RasGateHealthState.Ready;
    }
}