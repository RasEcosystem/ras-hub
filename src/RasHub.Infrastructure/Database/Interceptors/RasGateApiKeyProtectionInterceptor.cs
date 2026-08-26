using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RasHub.Domain;
using RasHub.Infrastructure.Database.Security;

namespace RasHub.Infrastructure.Database.Interceptors;

public sealed class RasGateApiKeyProtectionInterceptor(
    RasGateApiKeyProtector protector)
    : SaveChangesInterceptor, IMaterializationInterceptor
{
    private readonly ConditionalWeakTable<DbContext, PendingProtection>
        _pendingProtections = new();

    public object InitializedInstance(
        MaterializationInterceptionData materializationData,
        object entity)
    {
        if (entity is RasGate rasGate)
            rasGate.ApiKey = protector.Unprotect(rasGate.ApiKey);

        return entity;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ProtectPendingKeys(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ProtectPendingKeys(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        RestorePlaintext(eventData.Context, true);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        RestorePlaintext(eventData.Context, true);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        RestorePlaintext(eventData.Context, false);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RestorePlaintext(eventData.Context, false);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        RestorePlaintext(eventData.Context, false);
        base.SaveChangesCanceled(eventData);
    }

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RestorePlaintext(eventData.Context, false);
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    private void ProtectPendingKeys(DbContext? dbContext)
    {
        if (dbContext is null)
            return;

        var keys = dbContext.ChangeTracker
            .Entries<RasGate>()
            .Where(entry => entry.State == EntityState.Added ||
                            entry.Property(rasGate => rasGate.ApiKey).IsModified)
            .Select(entry => new PendingKey(
                entry.Property(rasGate => rasGate.ApiKey),
                entry.Property(rasGate => rasGate.ApiKey).CurrentValue))
            .ToArray();

        if (keys.Length == 0)
            return;

        _pendingProtections.Add(dbContext, new PendingProtection(keys));

        try
        {
            foreach (var key in keys)
                key.Property.CurrentValue = protector.Protect(key.Plaintext);
        }
        catch
        {
            RestorePlaintext(dbContext, false);
            throw;
        }
    }

    private void RestorePlaintext(DbContext? dbContext, bool succeeded)
    {
        if (dbContext is null ||
            !_pendingProtections.TryGetValue(dbContext, out var pending))
            return;

        _pendingProtections.Remove(dbContext);

        foreach (var key in pending.Keys)
        {
            var saveWasAccepted = succeeded &&
                                  key.Property.EntityEntry.State ==
                                  EntityState.Unchanged;
            key.Property.CurrentValue = key.Plaintext;

            if (!saveWasAccepted)
                continue;

            key.Property.OriginalValue = key.Plaintext;
            key.Property.IsModified = false;
        }
    }

    private sealed record PendingProtection(IReadOnlyList<PendingKey> Keys);

    private sealed record PendingKey(
        PropertyEntry<RasGate, string> Property,
        string Plaintext);
}
