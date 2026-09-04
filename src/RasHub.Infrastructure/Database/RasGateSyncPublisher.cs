using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database;

public sealed class RasGateSyncPublisher(RasHubDbContext db)
    : IRasGateSyncPublisher
{
    public async Task<bool> TryPublishStatusAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        RasGateStatus status,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var rasGate = await GetTrackedGateAsync(rasGateId, cancellationToken);

        if (rasGate is null)
            return false;

        PreparePublicationGuard(rasGate, expectedConfigurationRevision);
        rasGate.InstanceName = status.InstanceName;
        rasGate.Version = status.Version;
        rasGate.StatusObservedAt = observedAt;
        rasGate.RacAvailable = status.RacAvailable;
        rasGate.RacVersion = status.RacVersion;
        rasGate.RacStatusObservedAt = observedAt;
        rasGate.LastSeenAt = observedAt;

        return await TrySaveAsync(cancellationToken);
    }

    private async Task<RasGate?> GetTrackedGateAsync(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        return db.RasGates.Local.SingleOrDefault(item => item.Id == rasGateId) ??
               await db.RasGates
                   .IgnoreQueryFilters()
                   .SingleOrDefaultAsync(
                       item => item.Id == rasGateId,
                       cancellationToken);
    }

    private void PreparePublicationGuard(
        RasGate rasGate,
        long expectedConfigurationRevision)
    {
        var entry = db.Entry(rasGate);
        entry.Property(item => item.ConfigurationRevision).OriginalValue =
            expectedConfigurationRevision;
        entry.Property(item => item.IsActive).OriginalValue = true;
        entry.Property(item => item.IsDeleted).OriginalValue = false;
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException exception)
            when (exception.Entries.Any(entry => entry.Entity is RasGate))
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
