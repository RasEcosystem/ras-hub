using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database.Interceptors;

public sealed class RasGateConfigurationRevisionInterceptor
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyRevisions(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyRevisions(eventData.Context);
        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    private static void ApplyRevisions(DbContext? dbContext)
    {
        if (dbContext is null)
            return;

        foreach (var entry in dbContext.ChangeTracker.Entries<RasGate>())
        {
            if (entry.State != EntityState.Modified ||
                !RemoteIdentityChanged(entry))
                continue;

            var revision = entry.Property(item => item.ConfigurationRevision);
            revision.CurrentValue = checked(revision.OriginalValue + 1);
        }
    }

    private static bool RemoteIdentityChanged(EntityEntry<RasGate> entry)
    {
        return Changed(entry.Property(item => item.Url)) ||
               Changed(entry.Property(item => item.Port)) ||
               Changed(entry.Property(item => item.ApiKey));
    }

    private static bool Changed<T>(PropertyEntry<RasGate, T> property)
    {
        return property.IsModified &&
               !EqualityComparer<T>.Default.Equals(
                   property.OriginalValue,
                   property.CurrentValue);
    }
}