using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests.Search;
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
            ConfigurationRevision = rasGate.ConfigurationRevision,
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
            .Select(rasGate => new RasGateAdministrationItem
            {
                Id = rasGate.Id,
                Name = rasGate.Name,
                Url = rasGate.Url,
                Port = rasGate.Port,
                IsActive = rasGate.IsActive,
                ConfigurationRevision = rasGate.ConfigurationRevision,
                InstanceName = rasGate.InstanceName,
                Version = rasGate.Version,
                StatusObservedAt = rasGate.StatusObservedAt,
                RacAvailable = rasGate.RacAvailable,
                RacVersion = rasGate.RacVersion,
                RacStatusObservedAt = rasGate.RacStatusObservedAt,
                LastSeenAt = rasGate.LastSeenAt,
                CreatedAt = rasGate.CreatedAt,
                UpdatedAt = rasGate.UpdatedAt,
                IsDeleted = rasGate.IsDeleted,
                DeletedAt = rasGate.DeletedAt
            })
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
            Items = items, TotalCount = totalCount, Page = request.Page, PageSize = request.PageSize
        };
    }

    public async Task<PageResult<RasGateModel>> SearchPagedAsync(
        SearchRasGatesRequest search,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var query = ApplySearch(
            db.RasGates.AsNoTracking(),
            search);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(rasGate => rasGate.Name)
            .ThenBy(rasGate => rasGate.Id)
            .ApplyPagination(page.Page, page.PageSize)
            .Select(ModelProjection)
            .ToListAsync(cancellationToken);

        return new PageResult<RasGateModel>
        {
            Items = items, TotalCount = totalCount, Page = page.Page, PageSize = page.PageSize
        };
    }

    public async Task<IReadOnlyList<RasGateModel>> SearchAllAsync(
        SearchRasGatesRequest search,
        CancellationToken cancellationToken)
    {
        return await ApplySearch(
                db.RasGates.AsNoTracking(),
                search)
            .OrderBy(rasGate => rasGate.Name)
            .ThenBy(rasGate => rasGate.Id)
            .Select(ModelProjection)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RasGateModel>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        return await db.RasGates
            .AsNoTracking()
            .OrderByDescending(rasGate => rasGate.CreatedAt)
            .ThenBy(rasGate => rasGate.Id)
            .Select(ModelProjection)
            .ToListAsync(cancellationToken);
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

    private static IQueryable<RasGate> ApplySearch(
        IQueryable<RasGate> query,
        SearchRasGatesRequest search)
    {
        var term = search.Query.Trim().ToUpperInvariant();
        var fields = search.Fields is { Length: > 0 }
            ? search.Fields.ToHashSet()
            : [RasGateSearchField.Name];
        var searchName = fields.Contains(RasGateSearchField.Name);
        var searchUrl = fields.Contains(RasGateSearchField.Url);

        return query.Where(rasGate =>
            (searchName && rasGate.Name.ToUpper().Contains(term)) ||
            (searchUrl && rasGate.Url.ToUpper().Contains(term)));
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

public sealed record RasGateAdministrationItem
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Url { get; init; }

    public required int Port { get; init; }

    public required bool IsActive { get; init; }

    public required long ConfigurationRevision { get; init; }

    public required string? InstanceName { get; init; }

    public required string? Version { get; init; }

    public required DateTime? StatusObservedAt { get; init; }

    public required bool? RacAvailable { get; init; }

    public required string? RacVersion { get; init; }

    public required DateTime? RacStatusObservedAt { get; init; }

    public required DateTime? LastSeenAt { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; init; }

    public required bool IsDeleted { get; init; }

    public required DateTime? DeletedAt { get; init; }

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
