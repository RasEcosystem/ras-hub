using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database.Interceptors;

public sealed class RasEndpointConfigurationRevisionInterceptor
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

        foreach (var entry in dbContext.ChangeTracker.Entries<RasEndpoint>())
        {
            if (entry.State != EntityState.Modified)
                continue;

            if (!Changed(entry.Property(endpoint => endpoint.Name)) &&
                !Changed(entry.Property(endpoint => endpoint.Host)) &&
                !Changed(entry.Property(endpoint => endpoint.Port)) &&
                !Changed(entry.Property(endpoint => endpoint.IsActive)) &&
                !Changed(entry.Property(endpoint => endpoint.IsDeleted)))
                continue;

            var revision = entry.Property(endpoint =>
                endpoint.ConfigurationRevision);
            revision.CurrentValue = checked(revision.OriginalValue + 1);
        }
    }

    private static bool Changed<T>(
        PropertyEntry<RasEndpoint, T> property)
    {
        return property.IsModified &&
               !EqualityComparer<T>.Default.Equals(
                   property.OriginalValue,
                   property.CurrentValue);
    }
}
