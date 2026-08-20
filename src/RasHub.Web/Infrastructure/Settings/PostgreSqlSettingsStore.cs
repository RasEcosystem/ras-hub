using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nava.Settings;
using Nava.Settings.Abstractions;
using RasHub.Infrastructure.Database;

namespace RasHub.Web.Infrastructure.Settings;

public sealed class PostgreSqlSettingsStore(
    RasHubDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<PostgreSqlSettingsStore> logger)
    : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public async Task<T?> GetAsync<T>(string? scope = null)
        where T : class
    {
        var key = ConfigurationKey.For<T>(scope);
        var value = await dbContext.Settings
            .AsNoTracking()
            .Where(entry => entry.Key == key)
            .Select(entry => entry.Value)
            .SingleOrDefaultAsync();

        if (value is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(value, JsonOptions);
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception,
                "Failed to deserialize settings {SettingsKey}",
                key);
            return null;
        }
    }

    public async Task SaveAsync<T>(T settings, string? scope = null)
        where T : class
    {
        var key = ConfigurationKey.For<T>(scope);
        var value = JsonSerializer.Serialize(settings, JsonOptions);
        var updatedAt = timeProvider.GetUtcNow();

        var updated = await dbContext.Settings
            .Where(entry => entry.Key == key)
            .ExecuteUpdateAsync(update => update
                .SetProperty(entry => entry.Value, value)
                .SetProperty(entry => entry.UpdatedAt, updatedAt));

        if (updated > 0)
        {
            LogChanged<T>("updated", scope);
            return;
        }

        dbContext.Settings.Add(new SettingEntry { Key = key, Value = value, UpdatedAt = updatedAt });

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();

            await dbContext.Settings
                .Where(entry => entry.Key == key)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(entry => entry.Value, value)
                    .SetProperty(entry => entry.UpdatedAt, updatedAt));

            LogChanged<T>("updated after a concurrent insert", scope);
            return;
        }

        LogChanged<T>("created", scope);
    }

    public async Task RemoveAsync<T>(string? scope = null)
        where T : class
    {
        var key = ConfigurationKey.For<T>(scope);

        var removed = await dbContext.Settings
            .Where(entry => entry.Key == key)
            .ExecuteDeleteAsync();

        if (removed > 0)
            LogChanged<T>("removed", scope);
    }

    private void LogChanged<T>(string operation, string? scope)
    {
        logger.LogInformation(
            "Settings {SettingsType} were {Operation}; scoped: {IsScoped}",
            typeof(T).Name,
            operation,
            scope is not null);
    }
}