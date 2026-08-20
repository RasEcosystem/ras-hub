using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database;

public sealed class RasInfobaseSnapshotStore(RasHubDbContext db)
    : IRasInfobaseSnapshotStore
{
    public async Task ApplyAsync(
        Guid rasClusterId,
        IReadOnlyList<RasInfobaseSnapshot> snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var storedInfobases = await db.RasInfobases
            .IgnoreQueryFilters()
            .Where(infobase => infobase.RasClusterId == rasClusterId)
            .ToListAsync(cancellationToken);
        var storedByExternalId = storedInfobases.ToDictionary(infobase => infobase.ExternalId);
        var observedExternalIds = new HashSet<Guid>();

        foreach (var item in snapshot)
        {
            if (!observedExternalIds.Add(item.ExternalId))
                throw new InvalidOperationException(
                    $"Infobase snapshot contains duplicate external ID " +
                    $"'{item.ExternalId}'.");

            if (!storedByExternalId.TryGetValue(
                    item.ExternalId,
                    out var infobase))
            {
                infobase = new RasInfobase
                {
                    RasClusterId = rasClusterId,
                    ExternalId = item.ExternalId,
                    Name = item.Name,
                    Description = item.Description
                };
                db.RasInfobases.Add(infobase);
            }

            Apply(infobase, item, observedAt);
        }

        foreach (var infobase in storedInfobases)
            if (!infobase.IsDeleted &&
                !observedExternalIds.Contains(infobase.ExternalId))
                db.RasInfobases.Remove(infobase);
    }

    public async Task UpsertAsync(
        Guid rasClusterId,
        RasInfobaseSnapshot snapshot,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var infobase = await db.RasInfobases
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.RasClusterId == rasClusterId &&
                        item.ExternalId == snapshot.ExternalId,
                cancellationToken);

        if (infobase is null)
        {
            infobase = new RasInfobase
            {
                RasClusterId = rasClusterId,
                ExternalId = snapshot.ExternalId,
                Name = snapshot.Name,
                Description = snapshot.Description
            };
            db.RasInfobases.Add(infobase);
        }

        Apply(infobase, snapshot, observedAt);
    }

    public async Task InvalidateAsync(
        Guid rasClusterId,
        CancellationToken cancellationToken)
    {
        var infobases = await db.RasInfobases
            .Where(infobase => infobase.RasClusterId == rasClusterId)
            .ToListAsync(cancellationToken);

        db.RasInfobases.RemoveRange(infobases);
    }

    private static void Apply(
        RasInfobase infobase,
        RasInfobaseSnapshot snapshot,
        DateTime observedAt)
    {
        infobase.Name = snapshot.Name;
        infobase.Description = snapshot.Description;
        infobase.ObservedAt = observedAt;
        infobase.IsDeleted = false;
        infobase.DeletedAt = null;
    }
}